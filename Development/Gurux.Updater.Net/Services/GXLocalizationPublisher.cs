//
// --------------------------------------------------------------------------
//  Gurux Ltd
//
//
//
// Filename:        $HeadURL$
//
// Version:         $Revision$,
//                  $Date$
//                  $Author$
//
// Copyright (c) Gurux Ltd
//
//---------------------------------------------------------------------------
//
//  DESCRIPTION
//
// This file is a part of Gurux Device Framework.
//
// Gurux Device Framework is Open Source software; you can redistribute it
// and/or modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation; version 2 of the License.
// Gurux Device Framework is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU General Public License for more details.
//
// This code is licensed under the GNU General Public License v2.
// Full text may be retrieved at http://www.gnu.org/licenses/gpl-2.0.txt
//---------------------------------------------------------------------------

using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Gurux.Updater.Model;
using NuGet.Versioning;

namespace Gurux.Updater.Services;

/// <summary>Shared deterministic source validation, planning and immutable publication for CLI, server and CI.</summary>
public sealed class GXLocalizationPublisher(HttpClient client)
{
    readonly GXLocalizationService service = new(client);

    /// <summary>Checks that a publishable checkout contains every referenced package, with matching identities and hashes.</summary>
    public async Task ValidateStoreAsync(string index, CancellationToken token = default)
    {
        if (Uri.TryCreate(index, UriKind.Absolute, out var uri) && !uri.IsFile) throw new ArgumentException("A store must be local.");
        index = Path.GetFullPath(index);
        var catalog = await service.GetCatalogAsync(index, token);
        string directory = Path.GetDirectoryName(index)!;
        foreach (var release in catalog.Items.SelectMany(x => x.Releases))
        {
            if (Uri.TryCreate(release.Path, UriKind.Absolute, out _) || Path.IsPathRooted(release.Path) || release.Path.Contains('\\'))
                throw new InvalidDataException("A published store must use safe relative package paths.");
            string path = GXLocalizationService.ManagedPath(directory, release.Path);
            if (!File.Exists(path)) throw new InvalidDataException($"Missing published package: {release.Path}");
        }
        await service.ValidateAllAsync(index, token);
    }

    /// <summary>Creates a validated source snapshot without writing files.</summary>
    public async Task<GXLocalizationSnapshot> ReadSourcesAsync(IEnumerable<GXLocalizationSource> sources, CancellationToken token = default)
    {
        var snapshot = new GXLocalizationSnapshot();
        foreach (var source in sources)
        {
            string directory = Path.GetFullPath(source.Directory);
            GXLocalizationService.RejectLink(directory);
            if (!Directory.Exists(directory)) throw new InvalidDataException($"Localization directory does not exist: {directory}");
            var metadata = await ProjectMetadataAsync(source, directory, token);
            string owner = source.Owner ?? metadata.GetValueOrDefault("PackageId") ?? metadata.GetValueOrDefault("AssemblyName") ?? "";
            string type = source.OwnerType ?? metadata.GetValueOrDefault("LocalizationOwnerType") ??
                (metadata.GetValueOrDefault("OutputType") is "Exe" or "WinExe" ? "application" : "");
            string culture = GXLocalizationJson.Culture(source.DefaultCulture ?? metadata.GetValueOrDefault("LocalizationDefaultCulture") ??
                metadata.GetValueOrDefault("NeutralLanguage") ?? throw new InvalidDataException($"defaultCulture is required for '{directory}'; specify --default-culture or LocalizationDefaultCulture."));
            GXLocalizationJson.ValidateOwner(owner, type);
            HashSet<string> cultures = new(StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
            {
                if (Path.GetFileName(path) == "localization.json") continue; // not required, not used as a metadata source
                GXLocalizationService.RejectLink(path);
                string name = GXLocalizationJson.Culture(Path.GetFileNameWithoutExtension(path));
                if (!cultures.Add(name)) throw new InvalidDataException($"Duplicate normalized culture '{name}' in '{directory}'.");
                var resources = GXLocalizationJson.Read<Dictionary<string, string>>(await File.ReadAllBytesAsync(path, token), path);
                var package = new GXLocalizationPackage { SchemaVersion = 2, Owner = owner, OwnerType = type, Culture = name,
                    Version = "1.0.0", DefaultCulture = culture, OwnerVersionRange = source.OwnerVersionRange ?? metadata.GetValueOrDefault("LocalizationOwnerVersionRange"),
                    Resources = new(StringComparer.Ordinal) { [name] = resources } };
                GXLocalizationJson.ValidatePackage(package);
                snapshot.Packages.Add(package);
            }
            if (cultures.Count == 0) throw new InvalidDataException($"No culture JSON files found in '{directory}'.");
        }
        ValidateSnapshot(snapshot);
        return snapshot;
    }

    static async Task<Dictionary<string, string>> ProjectMetadataAsync(GXLocalizationSource source, string directory, CancellationToken token)
    {
        if (source.MetadataPackage != null)
        {
            if (source.Project != null) throw new InvalidDataException("Choose project or metadataPackage for inference.");
            using var archive = ZipFile.OpenRead(source.MetadataPackage);
            var specifications = archive.Entries.Where(x => x.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)).ToList();
            if (specifications.Count > 1) throw new InvalidDataException("Metadata package has ambiguous NuGet specifications.");
            if (specifications.Count == 1)
            {
                using var stream = specifications[0].Open();
                using var xml = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                var specification = XDocument.Load(xml);
                var metadata = specification.Root?.Elements().SingleOrDefault(x => x.Name.LocalName == "metadata") ?? throw new InvalidDataException("NuGet metadata is missing.");
                string? Value(string name) => metadata.Elements().SingleOrDefault(x => x.Name.LocalName == name)?.Value;
                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                if (Value("id") is { Length: > 0 } id) result["PackageId"] = id;
                if (Value("language") is { Length: > 0 } language) result["NeutralLanguage"] = language;
                var types = metadata.Descendants().Where(x => x.Name.LocalName == "packageType").Select(x => x.Attribute("name")?.Value).Where(x => x is "application" or "module").Distinct().ToList();
                if (types.Count == 1) result["LocalizationOwnerType"] = types[0]!;
                return result;
            }
            var info = GXCatalogPackageReader.Read(source.MetadataPackage);
            return new(StringComparer.Ordinal) { ["PackageId"] = info.Id, ["LocalizationOwnerType"] = info.Type.ToString().ToLowerInvariant() };
        }
        string? project = source.Project;
        if (project == null)
        {
            for (var current = new DirectoryInfo(directory); current != null; current = current.Parent)
            {
                var projects = current.GetFiles("*.csproj");
                if (projects.Length > 1) break; // explicit metadata is still allowed
                if (projects.Length == 1) { project = projects[0].FullName; break; }
            }
        }
        if (project == null) return new(StringComparer.Ordinal);
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { "msbuild", Path.GetFullPath(project), "-nologo", "-getProperty:PackageId,AssemblyName,OutputType,LocalizationOwnerType,LocalizationDefaultCulture,NeutralLanguage,LocalizationOwnerVersionRange" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidDataException("Cannot evaluate project metadata.");
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        if (process.ExitCode != 0) throw new InvalidDataException($"Project metadata evaluation failed: {await error}");
        using var document = JsonDocument.Parse(await output);
        return document.RootElement.GetProperty("Properties").EnumerateObject().Where(x => x.Value.GetString() is { Length: > 0 })
            .ToDictionary(x => x.Name, x => x.Value.GetString()!, StringComparer.Ordinal);
    }

    /// <summary>Hashes parsed resources and effective metadata with ordinal key sorting and exact string values.</summary>
    public static string ContentHash(GXLocalizationPackage package)
    {
        GXLocalizationJson.ValidatePackage(package);
        if (package.SchemaVersion != 2) throw new InvalidDataException("Content hashes apply to schema 2 language packages.");
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject(); writer.WriteNumber("schemaVersion", 2);
            writer.WriteString("owner", package.Owner); writer.WriteString("ownerType", package.OwnerType);
            writer.WriteString("culture", package.Culture); writer.WriteString("defaultCulture", package.DefaultCulture);
            writer.WriteString("ownerVersionRange", package.OwnerVersionRange == null ? null : VersionRange.Parse(package.OwnerVersionRange).ToNormalizedString());
            writer.WriteString("description", GXLocalizationJson.EffectiveDescription(package.Owner, package.OwnerType, package.Description));
            writer.WriteString("releaseNotes", package.ReleaseNotes);
            writer.WritePropertyName("resources"); writer.WriteStartObject();
            foreach (var pair in package.Resources[package.Culture!].OrderBy(x => x.Key, StringComparer.Ordinal)) writer.WriteString(pair.Key, pair.Value);
            writer.WriteEndObject(); writer.WriteEndObject();
        }
        return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
    }

    static void ValidateSnapshot(GXLocalizationSnapshot snapshot)
    {
        if (snapshot.SchemaVersion != 2 || snapshot.Packages == null || snapshot.Packages.Count == 0) throw new InvalidDataException("A schema 2 snapshot must contain packages.");
        HashSet<string> identities = new(StringComparer.OrdinalIgnoreCase);
        foreach (var package in snapshot.Packages)
        {
            if (package == null || package.SchemaVersion != 2) throw new InvalidDataException("Snapshots accept only schema 2 source packages.");
            GXLocalizationJson.ValidatePackage(package);
            if (!identities.Add(package.Owner + "/" + package.Culture)) throw new InvalidDataException("Duplicate owner/culture source registration.");
        }
        foreach (var group in snapshot.Packages.GroupBy(x => x.Owner, StringComparer.OrdinalIgnoreCase))
            if (group.Select(x => (x.Owner, x.OwnerType, x.DefaultCulture, x.OwnerVersionRange)).Distinct().Count() != 1)
                throw new InvalidDataException("Source owner casing, type and shared metadata must agree.");
    }

    /// <summary>Plans all sources against the latest committed history without creating files or locks.</summary>
    public async Task<GXLocalizationPublicationPlan> PlanAsync(IEnumerable<GXLocalizationSource> sources, string index, CancellationToken token = default) =>
        await PlanSnapshotAsync(await ReadSourcesAsync(sources, token), index, token);

    /// <summary>Plans a validated snapshot against a local or remote index.</summary>
    public async Task<GXLocalizationPublicationPlan> PlanSnapshotAsync(GXLocalizationSnapshot snapshot, string index, CancellationToken token = default)
    {
        ValidateSnapshot(snapshot);
        var catalog = await ReadHistoryAsync(index, token);
        return BuildPlan(snapshot, catalog);
    }

    async Task<GXLocalizationCatalog> ReadHistoryAsync(string index, CancellationToken token) =>
        !Uri.TryCreate(index, UriKind.Absolute, out var uri) || uri.IsFile
            ? File.Exists(index) ? await service.GetCatalogAsync(index, token) : new()
            : await new GXLocalizationService(client, allowHttp: true).GetCatalogAsync(index, token);

    static GXLocalizationPublicationPlan BuildPlan(GXLocalizationSnapshot snapshot, GXLocalizationCatalog catalog)
    {
        var plan = new GXLocalizationPublicationPlan();
        foreach (var package in snapshot.Packages.OrderBy(x => x.Owner, StringComparer.Ordinal).ThenBy(x => x.Culture, StringComparer.Ordinal))
        {
            var item = catalog.Items.SingleOrDefault(x => string.Equals(x.Owner, package.Owner, StringComparison.OrdinalIgnoreCase));
            if (item != null && (item.Owner != package.Owner || item.OwnerType != package.OwnerType)) throw new InvalidDataException("Published owner identity/type conflict.");
            var latest = item?.Releases.Where(x => x.Culture == package.Culture).OrderByDescending(x => GXLocalizationJson.Version(x.Version), VersionComparer.VersionRelease).FirstOrDefault();
            string hash = ContentHash(package);
            bool changed = latest == null || latest.ContentSha256 != hash;
            var version = latest == null ? new NuGetVersion(1, 0, 0) : GXLocalizationJson.Version(latest.Version);
            if (changed && latest != null) version = new NuGetVersion(version.Major, version.Minor, checked(version.Patch + 1));
            plan.Languages.Add(new(package.Owner, package.Culture!, version.ToNormalizedString(), changed, hash));
            var defaults = snapshot.Packages.FirstOrDefault(x => x.Owner == package.Owner && x.Culture == package.DefaultCulture);
            if (defaults != null)
                foreach (string key in defaults.Resources[defaults.Culture!].Keys.Except(package.Resources[package.Culture!].Keys, StringComparer.Ordinal))
                    plan.Warnings.Add($"{package.Owner}/{package.Culture}: missing default key '{key}'.");
        }
        foreach (var group in snapshot.Packages.GroupBy(x => x.Owner))
        {
            var previous = catalog.Items.SingleOrDefault(x => x.Owner == group.Key);
            foreach (string culture in previous?.Releases.Where(x => x.Culture != null).Select(x => x.Culture!).Distinct() ?? [])
                if (!group.Any(x => x.Culture == culture)) plan.Warnings.Add($"{group.Key}/{culture}: source removed; published history and installed language retained.");
        }
        return plan;
    }

    /// <summary>Publishes a validated source snapshot using a cross-process lock and index-last atomic commit.</summary>
    public async Task<GXLocalizationPublicationPlan> PublishAsync(IEnumerable<GXLocalizationSource> sources, string index, CancellationToken token = default) =>
        await PublishSnapshotAsync(await ReadSourcesAsync(sources, token), index, token);

    /// <summary>Server/CI entry point. Replans under the lock; retries cannot allocate duplicate releases.</summary>
    public async Task<GXLocalizationPublicationPlan> PublishSnapshotAsync(GXLocalizationSnapshot snapshot, string index, CancellationToken token = default)
    {
        ValidateSnapshot(snapshot);
        if (Uri.TryCreate(index, UriKind.Absolute, out var uri) && !uri.IsFile) throw new ArgumentException("Publication storage must be local. GitHub Pages accepts no uploads.");
        index = Path.GetFullPath(index);
        string directory = Path.GetDirectoryName(index)!;
        GXLocalizationService.ManagedPath(directory, Path.GetFileName(index));
        Directory.CreateDirectory(directory);
        string lockPath = GXLocalizationService.ManagedPath(directory, Path.GetFileName(index) + ".lock");
        await using var held = await LockAsync(lockPath, token);
        var catalog = await ReadHistoryAsync(index, token);
        // Validate historical bytes as well as metadata before trusting the change hashes.
        if (File.Exists(index)) await ValidateStoreAsync(index, token);
        var plan = BuildPlan(snapshot, catalog);
        if (!plan.Languages.Any(x => x.Changed)) return plan;
        var files = new List<(string Target, byte[] Bytes)>();
        foreach (var language in plan.Languages.Where(x => x.Changed))
        {
            var input = snapshot.Packages.Single(x => x.Owner == language.Owner && x.Culture == language.Culture);
            var package = new GXLocalizationPackage { SchemaVersion = 2, Culture = input.Culture, Owner = input.Owner, OwnerType = input.OwnerType,
                DefaultCulture = input.DefaultCulture, OwnerVersionRange = input.OwnerVersionRange, Description = input.Description, ReleaseNotes = input.ReleaseNotes,
                Version = language.Version, Resources = input.Resources.ToDictionary(x => x.Key, x => x.Value.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal), StringComparer.Ordinal) };
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(package, GXLocalizationJson.Options);
            string relative = $"packages/{package.Owner}/{package.Culture}/{package.Version}.json";
            string target = GXLocalizationService.ManagedPath(directory, relative);
            if (File.Exists(target) && !(await File.ReadAllBytesAsync(target, token)).SequenceEqual(bytes))
                throw new InvalidDataException($"Immutable package collision: {target}");
            files.Add((target, bytes));
            var item = catalog.Items.SingleOrDefault(x => x.Owner == package.Owner);
            if (item == null) { item = new() { Owner = package.Owner, OwnerType = package.OwnerType }; catalog.Items.Add(item); }
            item.Releases.Add(new() { Culture = package.Culture, Cultures = [package.Culture!], Version = package.Version,
                DefaultCulture = package.DefaultCulture, OwnerVersionRange = package.OwnerVersionRange, Description = package.Description, ReleaseNotes = package.ReleaseNotes,
                ContentSha256 = language.ContentSha256, Path = relative, Size = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() });
        }
        catalog.GeneratedAt = DateTimeOffset.UtcNow;
        GXLocalizationJson.ValidateCatalog(catalog);
        string temporary = index + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(catalog, GXLocalizationJson.Options), token);
            foreach (var file in files)
            {
                if (File.Exists(file.Target)) continue; // exact orphan from interrupted prior attempt
                Directory.CreateDirectory(Path.GetDirectoryName(file.Target)!);
                string staged = file.Target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { await File.WriteAllBytesAsync(staged, file.Bytes, token); File.Move(staged, file.Target); }
                finally { File.Delete(staged); }
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, index, true); // Readers see either complete old or complete new history.
        }
        finally { File.Delete(temporary); }
        return plan;
    }

    internal static async Task<FileStream> LockAsync(string path, CancellationToken token)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < deadline) { await Task.Delay(50, token); }
        }
    }

    /// <summary>Idempotent immutable legacy upload, sharing the consumer's version parser and validation.</summary>
    public async Task<GXLocalizationCatalog> PublishLegacyAsync(string packagePath, string index, CancellationToken token = default)
    {
        var package = await service.ReadPackageAsync(packagePath, token);
        if (package.SchemaVersion != 1) throw new InvalidDataException("Single-culture publications require a source snapshot.");
        index = Path.GetFullPath(index);
        string directory = Path.GetDirectoryName(index)!;
        GXLocalizationService.ManagedPath(directory, Path.GetFileName(index));
        Directory.CreateDirectory(directory);
        // AddAsync owns index.lock; this outer lock serializes check-and-add for legacy requests.
        await using var held = await LockAsync(GXLocalizationService.ManagedPath(directory, Path.GetFileName(index) + ".request.lock"), token);
        var catalog = await ReadHistoryAsync(index, token);
        var item = catalog.Items.SingleOrDefault(x => string.Equals(x.Owner, package.Owner, StringComparison.OrdinalIgnoreCase));
        if (item != null && (item.Owner != package.Owner || item.OwnerType != package.OwnerType || item.Releases.Any(x => x.Culture != null)))
            throw new InvalidOperationException("Localization owner identity/type conflict or owner migrated to language packages.");
        var previous = item?.Releases.SingleOrDefault(x => VersionComparer.VersionRelease.Equals(GXLocalizationJson.Version(x.Version), GXLocalizationJson.Version(package.Version)));
        if (previous != null)
        {
            byte[] bytes = await File.ReadAllBytesAsync(packagePath, token);
            if (previous.Size == bytes.Length && previous.Sha256.Equals(Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase)) return catalog;
            throw new InvalidOperationException("Published versions are immutable; increase version.");
        }
        return await service.AddAsync(index, packagePath, cancellationToken: token);
    }
}
