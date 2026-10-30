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

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gurux.Updater.Model;
using NuGet.Versioning;

namespace Gurux.Updater.Services;

/// <summary>Builds and imports independent tag-addressed content releases. Network/git transport belongs to the workflow.</summary>
public sealed class GXContentReleaseService(HttpClient client)
{
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static string Identity(GXContentManifest value) => $"{value.Kind}/{value.Owner}/{value.Unit}";
    static byte[] Json(object value) => JsonSerializer.SerializeToUtf8Bytes(value, GXLocalizationJson.Options);

    /// <summary>Reads strict tag metadata, checking wire identities against the derived safe paths.</summary>
    public static GXContentManifest ReadManifest(byte[] bytes)
    {
        var manifest = GXLocalizationJson.Read<GXContentManifest>(bytes, "content manifest");
        ValidateManifest(manifest);
        using var document = JsonDocument.Parse(bytes);
        foreach (var expected in new[] { ("tag", manifest.Tag), ("package", manifest.Package), ("path", manifest.Path) })
            if (!document.RootElement.TryGetProperty(expected.Item1, out var value) || value.GetString() != expected.Item2)
                throw new InvalidDataException("Content tag/package/path differs from its identity.");
        return manifest;
    }

    /// <summary>Reads a strict array of annotated-tag manifests.</summary>
    public static List<GXContentManifest> ReadHistory(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        GXLocalizationJson.CheckDuplicateProperties(document.RootElement, "$", "content tag history");
        return document.RootElement.EnumerateArray().Select(x => ReadManifest(System.Text.Encoding.UTF8.GetBytes(x.GetRawText()))).ToList();
    }

    /// <summary>Strictly validates a release manifest before it can select paths or tags.</summary>
    public static void ValidateManifest(GXContentManifest manifest)
    {
        GXLocalizationJson.ValidateOwner(manifest.Owner, manifest.OwnerType);
        GXLocalizationJson.ValidateOwner(manifest.Unit, "application");
        if (manifest.SchemaVersion != 1 || manifest.Kind is not ("localization" or "resources") ||
            !Regex.IsMatch(manifest.Version, @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z") ||
            manifest.Size <= 0 || !Regex.IsMatch(manifest.Sha256, @"\A[0-9a-f]{64}\z") || !Regex.IsMatch(manifest.ContentSha256, @"\A[0-9a-f]{64}\z"))
            throw new InvalidDataException("Invalid immutable content release manifest.");
        _ = GXLocalizationJson.Version(manifest.Version);
        if (manifest.Kind == "localization" && GXLocalizationJson.Culture(manifest.Unit) != manifest.Unit)
            throw new InvalidDataException("Noncanonical release culture.");
        if (manifest.OwnerVersionRange != null && (!VersionRange.TryParse(manifest.OwnerVersionRange, out var range) || range.IsFloating))
            throw new InvalidDataException("Invalid content compatibility range.");
    }

    /// <summary>Reads explicit sources, resolving paths beside the configuration file.</summary>
    public static List<GXContentSource> ReadSources(string path)
    {
        var result = GXLocalizationJson.Read<Configuration>(File.ReadAllBytes(path), path).Sources ?? throw new InvalidDataException("sources must be an array.");
        string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
        foreach (var source in result)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.Directory))
                throw new InvalidDataException("Each source needs a directory.");
            source.Directory = System.IO.Path.GetFullPath(source.Directory, directory);
            if (source.Project != null)
                source.Project = System.IO.Path.GetFullPath(source.Project, directory);
            if (source.MetadataPackage != null)
                source.MetadataPackage = System.IO.Path.GetFullPath(source.MetadataPackage, directory);
        }
        return result;
    }
    sealed class Configuration { [System.Text.Json.Serialization.JsonRequired] public List<GXContentSource> Sources { get; init; } = []; }

    /// <summary>Compares each registered unit with its latest reserved/published tag. Pending tags must be retried before calling.</summary>
    public async Task<List<GXPreparedContent>> PrepareAsync(IEnumerable<GXContentSource> sources, IEnumerable<GXContentManifest> history, CancellationToken token = default,
        ICollection<string>? warnings = null)
    {
        var previous = history.ToList();
        foreach (var manifest in previous)
            ValidateManifest(manifest);
        if (previous.Select(x => x.Tag).Distinct(StringComparer.OrdinalIgnoreCase).Count() != previous.Count)
            throw new InvalidDataException("Duplicate content tag identity.");
        var initial = await BuildUnitsAsync(sources, "1.0.0", token);
        foreach (string identity in previous.Select(Identity).Distinct(StringComparer.Ordinal))
            if (!initial.Any(x => Identity(x.Manifest) == identity))
                warnings?.Add($"Source removed: {identity}; tags, published packages and installed content retained.");
        List<GXPreparedContent> prepared = [];
        foreach (var package in initial)
        {
            if (previous.Concat(initial.Select(x => x.Manifest)).Any(x => string.Equals(x.Owner, package.Manifest.Owner, StringComparison.OrdinalIgnoreCase) &&
                (x.Owner != package.Manifest.Owner || x.OwnerType != package.Manifest.OwnerType)))
                throw new InvalidDataException("Content owner identity/type conflict.");
            if (previous.Any(x => string.Equals(Identity(x), Identity(package.Manifest), StringComparison.OrdinalIgnoreCase) && Identity(x) != Identity(package.Manifest)))
                throw new InvalidDataException("Content unit casing conflicts with retained tag identity.");
            var latest = previous.Where(x => Identity(x) == Identity(package.Manifest)).OrderByDescending(x => GXLocalizationJson.Version(x.Version), VersionComparer.VersionRelease).FirstOrDefault();
            if (latest?.ContentSha256 == package.Manifest.ContentSha256)
                continue;
            var version = latest == null ? new NuGetVersion(1, 0, 0) : GXLocalizationJson.Version(latest.Version);
            if (latest != null)
                version = new(version.Major, version.Minor, checked(version.Patch + 1));
            prepared.Add(await ReversionAsync(package, version.ToNormalizedString(), token));
        }
        return prepared;
    }

    /// <summary>Rebuilds at the tag's pinned source revision and rejects drift rather than allocating a new version.</summary>
    public async Task<GXPreparedContent> RebuildAsync(IEnumerable<GXContentSource> sources, GXContentManifest expected, CancellationToken token = default)
    {
        ValidateManifest(expected);
        var packages = await BuildUnitsAsync(sources, expected.Version, token);
        var package = packages.SingleOrDefault(x => Identity(x.Manifest) == Identity(expected)) ?? throw new InvalidDataException("Tag unit is not registered at its source revision.");
        if (package.Manifest.ContentSha256 != expected.ContentSha256 || package.Manifest.Sha256 != expected.Sha256 || package.Manifest.Size != expected.Size)
            throw new InvalidDataException("Tag source/package differs from immutable release manifest.");
        return package;
    }

    async Task<List<GXPreparedContent>> BuildUnitsAsync(IEnumerable<GXContentSource> sources, string version, CancellationToken token)
    {
        List<GXPreparedContent> result = [];
        foreach (var source in sources)
        {
            if (source.Kind == "localization")
            {
                var snapshot = await new GXLocalizationPublisher(client).ReadSourcesAsync([source], token);
                foreach (var input in snapshot.Packages)
                {
                    var package = new GXLocalizationPackage
                    {
                        SchemaVersion = 2,
                        Culture = input.Culture,
                        Owner = input.Owner,
                        OwnerType = input.OwnerType,
                        DefaultCulture = input.DefaultCulture,
                        OwnerVersionRange = input.OwnerVersionRange,
                        Version = version,
                        Resources = input.Resources.ToDictionary(x => x.Key, x => x.Value.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal))
                    };
                    result.Add(Prepare("localization", input.Owner, input.OwnerType, input.Culture!, version, input.OwnerVersionRange, GXLocalizationPublisher.ContentHash(package), Json(package)));
                }
            }
            else if (source.Kind == "resources")
            {
                GXLocalizationJson.ValidateOwner(source.Owner, source.OwnerType);
                GXLocalizationJson.ValidateOwner(source.Name, "application");
                string directory = System.IO.Path.GetFullPath(source.Directory);
                GXLocalizationService.RejectLink(directory);
                var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
                ReadDirectory(directory);
                void ReadDirectory(string current)
                {
                    GXLocalizationService.RejectLink(current);
                    foreach (string child in Directory.EnumerateDirectories(current))
                        ReadDirectory(child);
                    foreach (string path in Directory.EnumerateFiles(current))
                    {
                        GXLocalizationService.RejectLink(path);
                        string relative = System.IO.Path.GetRelativePath(directory, path).Replace('\\', '/');
                        ValidateResourcePath(relative);
                        if (!files.TryAdd(relative, File.ReadAllBytes(path)))
                            throw new InvalidDataException("Duplicate resource path.");
                    }
                }
                if (files.Count == 0)
                    throw new InvalidDataException("Resource source contains no files.");
                ValidateResourceNamespace(files.Keys.Append("resource-package.json"));
                string hash = ResourceHash(source.Owner!, source.OwnerType!, source.Name!, source.OwnerVersionRange, files);
                byte[] bytes = ResourceZip(source.Owner!, source.OwnerType!, source.Name!, source.OwnerVersionRange, version, files);
                result.Add(Prepare("resources", source.Owner!, source.OwnerType!, source.Name!, version, source.OwnerVersionRange, hash, bytes));
            }
            else
                throw new InvalidDataException("Content source kind must be localization or resources.");
        }
        if (result.Select(x => Identity(x.Manifest)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count)
            throw new InvalidDataException("Duplicate content source identity.");
        return result;
    }

    static GXPreparedContent Prepare(string kind, string owner, string type, string unit, string version, string? range, string hash, byte[] bytes)
    {
        var manifest = new GXContentManifest { Kind = kind, Owner = owner, OwnerType = type, Unit = unit, Version = version, OwnerVersionRange = range, ContentSha256 = hash, Sha256 = Hash(bytes), Size = bytes.Length };
        ValidateManifest(manifest);
        return new(manifest, bytes);
    }

    static void ValidateResourcePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Equals("resource-package.json", StringComparison.OrdinalIgnoreCase) || path.Contains('\\') || path.Contains(':') ||
            path.Any(x => x < 32 || "*?\"<>|".Contains(x)) ||
            path.Split('/').Any(x => x is "" or "." or ".." || x.EndsWith('.') || x.EndsWith(' ') || Regex.IsMatch(x.Split('.')[0], @"\A(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])\z", RegexOptions.IgnoreCase)))
            throw new InvalidDataException($"Unsafe resource path: {path}");
    }
    static void ValidateResourceNamespace(IEnumerable<string> paths)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            if (!files.Add(path) || directories.Contains(path))
                throw new InvalidDataException("Resource file/directory names collide.");
            int separator = path.IndexOf('/');
            while (separator >= 0)
            {
                string parent = path[..separator];
                if (files.Contains(parent))
                    throw new InvalidDataException("Resource file/directory names collide.");
                directories.Add(parent);
                separator = path.IndexOf('/', separator + 1);
            }
        }
    }
    static object ResourceMetadata(string owner, string type, string unit, string? range, SortedDictionary<string, byte[]> files) => new
    {
        schemaVersion = 1,
        owner,
        ownerType = type,
        name = unit,
        ownerVersionRange = range == null ? null : VersionRange.Parse(range).ToNormalizedString(),
        files = files.Select(x => new { path = x.Key, size = x.Value.Length, sha256 = Hash(x.Value) }).ToArray()
    };
    static string ResourceHash(string owner, string type, string unit, string? range, SortedDictionary<string, byte[]> files) => Hash(Json(ResourceMetadata(owner, type, unit, range, files)));
    static byte[] ResourceZip(string owner, string type, string unit, string? range, string version, SortedDictionary<string, byte[]> files)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var file in files)
                Add(file.Key, file.Value);
            Add("resource-package.json", Json(new { version, metadata = ResourceMetadata(owner, type, unit, range, files) }));
            void Add(string path, byte[] bytes)
            {
                var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
                entry.LastWriteTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                entry.ExternalAttributes = 0;
                using var stream = entry.Open();
                stream.Write(bytes);
            }
        }
        return output.ToArray();
    }

    async Task<GXPreparedContent> ReversionAsync(GXPreparedContent prepared, string version, CancellationToken token)
    {
        var manifest = prepared.Manifest;
        if (manifest.Kind == "localization")
        {
            var input = GXLocalizationJson.Read<GXLocalizationPackage>(prepared.Bytes, "package");
            var package = new GXLocalizationPackage
            {
                SchemaVersion = 2,
                Culture = input.Culture,
                Owner = input.Owner,
                OwnerType = input.OwnerType,
                DefaultCulture = input.DefaultCulture,
                OwnerVersionRange = input.OwnerVersionRange,
                Resources = input.Resources,
                Version = version
            };
            return Prepare(manifest.Kind, manifest.Owner, manifest.OwnerType, manifest.Unit, version, manifest.OwnerVersionRange, manifest.ContentSha256, Json(package));
        }
        var files = ResourceFiles(manifest, prepared.Bytes, checkVersion: false);
        await Task.CompletedTask;
        token.ThrowIfCancellationRequested();
        return Prepare(manifest.Kind, manifest.Owner, manifest.OwnerType, manifest.Unit, version, manifest.OwnerVersionRange, manifest.ContentSha256,
            ResourceZip(manifest.Owner, manifest.OwnerType, manifest.Unit, manifest.OwnerVersionRange, version, files));
    }

    static SortedDictionary<string, byte[]> ResourceFiles(GXContentManifest manifest, byte[] bytes, bool checkVersion = true)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        byte[]? metadata = null;
        foreach (var entry in archive.Entries)
        {
            using var buffer = new MemoryStream();
            using (var stream = entry.Open())
                stream.CopyTo(buffer);
            if (entry.FullName == "resource-package.json")
            { if (metadata != null) throw new InvalidDataException("Duplicate resource metadata."); metadata = buffer.ToArray(); }
            else
            { ValidateResourcePath(entry.FullName); if (!files.TryAdd(entry.FullName, buffer.ToArray())) throw new InvalidDataException("Duplicate resource ZIP entry."); }
        }
        if (metadata == null || files.Count == 0)
            throw new InvalidDataException("Resource package metadata/files missing.");
        ValidateResourceNamespace(files.Keys.Append("resource-package.json"));
        using var document = JsonDocument.Parse(metadata);
        using var expectedMetadata = JsonDocument.Parse(Json(ResourceMetadata(manifest.Owner, manifest.OwnerType, manifest.Unit, manifest.OwnerVersionRange, files)));
        GXLocalizationJson.CheckDuplicateProperties(document.RootElement, "$", "resource package");
        if (checkVersion && document.RootElement.GetProperty("version").GetString() != manifest.Version ||
            !JsonElement.DeepEquals(document.RootElement.GetProperty("metadata"), expectedMetadata.RootElement) ||
            ResourceHash(manifest.Owner, manifest.OwnerType, manifest.Unit, manifest.OwnerVersionRange, files) != manifest.ContentSha256)
            throw new InvalidDataException("Resource identity/content does not match manifest.");
        return files;
    }

    /// <summary>Validates exact artifact size, SHA and semantic identity before storage.</summary>
    public static void ValidatePackage(GXContentManifest manifest, byte[] bytes)
    {
        ValidateManifest(manifest);
        if (bytes.LongLength != manifest.Size || Hash(bytes) != manifest.Sha256)
            throw new InvalidDataException("Release package size/SHA-256 mismatch.");
        if (manifest.Kind == "resources")
        { _ = ResourceFiles(manifest, bytes); return; }
        var package = GXLocalizationJson.Read<GXLocalizationPackage>(bytes, manifest.Package);
        GXLocalizationJson.ValidatePackage(package);
        if (package.SchemaVersion != 2 || package.Owner != manifest.Owner || package.OwnerType != manifest.OwnerType || package.Culture != manifest.Unit ||
            package.Version != manifest.Version || package.OwnerVersionRange != manifest.OwnerVersionRange || GXLocalizationPublisher.ContentHash(package) != manifest.ContentSha256)
            throw new InvalidDataException("Release package identity/content does not match manifest.");
    }

    /// <summary>Imports an already published Release into immutable local history. No versions are allocated here.</summary>
    public async Task ImportAsync(GXContentManifest manifest, byte[] bytes, string root, CancellationToken token = default)
    {
        ValidatePackage(manifest, bytes);
        string directory = System.IO.Path.Combine(System.IO.Path.GetFullPath(root), manifest.Kind);
        string index = GXLocalizationService.ManagedPath(directory, manifest.Kind == "localization" ? "localization-index.json" : "resource-index.json");
        Directory.CreateDirectory(directory);
        await using var held = await GXLocalizationPublisher.LockAsync(GXLocalizationService.ManagedPath(directory, System.IO.Path.GetFileName(index) + ".lock"), token);
        string target = GXLocalizationService.ManagedPath(directory, manifest.Path);
        byte[] indexBytes;
        if (manifest.Kind == "localization")
        {
            if (File.Exists(index))
                await new GXLocalizationPublisher(client).ValidateStoreAsync(index, token);
            var catalog = File.Exists(index) ? await new GXLocalizationService(client).GetCatalogAsync(index, token) : new();
            var package = GXLocalizationJson.Read<GXLocalizationPackage>(bytes, manifest.Package);
            var item = catalog.Items.SingleOrDefault(x => string.Equals(x.Owner, manifest.Owner, StringComparison.OrdinalIgnoreCase));
            if (item != null && (item.Owner != manifest.Owner || item.OwnerType != manifest.OwnerType))
                throw new InvalidDataException("Catalog owner identity conflict.");
            if (item == null)
            { item = new() { Owner = manifest.Owner, OwnerType = manifest.OwnerType }; catalog.Items.Add(item); }
            var previous = item.Releases.SingleOrDefault(x => x.Culture == manifest.Unit && VersionComparer.VersionRelease.Equals(GXLocalizationJson.Version(x.Version), GXLocalizationJson.Version(manifest.Version)));
            if (previous != null)
            { await ExistingAsync(previous.Sha256, target, manifest, bytes, token); return; }
            item.Releases.Add(new()
            {
                Culture = manifest.Unit,
                Cultures = [manifest.Unit],
                Version = manifest.Version,
                OwnerVersionRange = manifest.OwnerVersionRange,
                DefaultCulture = package.DefaultCulture,
                Description = package.Description,
                ReleaseNotes = package.ReleaseNotes,
                Path = manifest.Path,
                Sha256 = manifest.Sha256,
                ContentSha256 = manifest.ContentSha256,
                Size = manifest.Size
            });
            catalog.GeneratedAt = DateTimeOffset.UtcNow;
            GXLocalizationJson.ValidateCatalog(catalog);
            indexBytes = Json(catalog);
        }
        else
        {
            if (File.Exists(index))
                await ValidateResourceStoreAsync(root, token);
            var catalog = File.Exists(index) ? GXLocalizationJson.Read<GXResourceCatalog>(await File.ReadAllBytesAsync(index, token), index) : new();
            ValidateResourceCatalog(catalog);
            var previous = catalog.Items.SingleOrDefault(x => x.Tag == manifest.Tag);
            if (previous != null)
            { await ExistingAsync(previous.Sha256, target, manifest, bytes, token); return; }
            if (catalog.Items.Any(x => string.Equals(x.Owner, manifest.Owner, StringComparison.OrdinalIgnoreCase) && (x.Owner != manifest.Owner || x.OwnerType != manifest.OwnerType)))
                throw new InvalidDataException("Resource owner conflict.");
            catalog.Items.Add(manifest);
            ValidateResourceCatalog(catalog);
            indexBytes = Json(catalog);
        }
        if (File.Exists(target) && !(await File.ReadAllBytesAsync(target, token)).SequenceEqual(bytes))
            throw new InvalidDataException("Immutable package collision.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
        if (!File.Exists(target))
            await AtomicWriteAsync(target, bytes, false, token);
        await AtomicWriteAsync(index, indexBytes, true, token);
    }

    static async Task ExistingAsync(string hash, string target, GXContentManifest manifest, byte[] bytes, CancellationToken token)
    {
        if (hash != manifest.Sha256 || !File.Exists(target) || !(await File.ReadAllBytesAsync(target, token)).SequenceEqual(bytes))
            throw new InvalidDataException("Immutable release conflict.");
    }
    static async Task AtomicWriteAsync(string path, byte[] bytes, bool overwrite, CancellationToken token)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        { await File.WriteAllBytesAsync(temporary, bytes, token); File.Move(temporary, path, overwrite); }
        finally { File.Delete(temporary); }
    }
    static void ValidateResourceCatalog(GXResourceCatalog catalog)
    {
        if (catalog.SchemaVersion != 1 || catalog.Items == null)
            throw new InvalidDataException("Invalid resource index.");
        foreach (var manifest in catalog.Items)
        { ValidateManifest(manifest); if (manifest.Kind != "resources") throw new InvalidDataException("Invalid resource index kind."); }
        if (catalog.Items.Select(x => x.Tag).Distinct(StringComparer.OrdinalIgnoreCase).Count() != catalog.Items.Count)
            throw new InvalidDataException("Duplicate resource release.");
    }

    /// <summary>Validates all retained resource packages before Pages copies its index.</summary>
    public static async Task ValidateResourceStoreAsync(string root, CancellationToken token = default)
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetFullPath(root), "resources");
        string index = System.IO.Path.Combine(directory, "resource-index.json");
        var catalog = GXLocalizationJson.Read<GXResourceCatalog>(await File.ReadAllBytesAsync(index, token), index);
        ValidateResourceCatalog(catalog);
        foreach (var manifest in catalog.Items)
            ValidatePackage(manifest, await File.ReadAllBytesAsync(GXLocalizationService.ManagedPath(directory, manifest.Path), token));
    }
}
