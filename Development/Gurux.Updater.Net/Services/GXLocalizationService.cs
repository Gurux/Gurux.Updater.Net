using System.Security.Cryptography;
using System.IO.Compression;
using System.Text.Json;
using Gurux.Updater.Model;
using NuGet.Versioning;

namespace Gurux.Updater.Services;

/// <summary>Manages complete JSON localization packages without importing or merging AMI resources.</summary>
public sealed class GXLocalizationService
{
    private readonly GXCatalogSourceReader reader;
    private readonly HttpClient client;

    /// <summary>Creates a localization service using the supplied HTTP client.</summary>
    public GXLocalizationService(HttpClient client, bool allowHttp = false, bool allowLoopbackHttp = false)
    {
        this.client = client;
        reader = new GXCatalogSourceReader(client, allowLoopbackHttp: allowLoopbackHttp, allowHttp: allowHttp);
    }

    /// <summary>Reads and validates a local or HTTPS localization index.</summary>
    public async Task<GXLocalizationCatalog> GetCatalogAsync(string source, CancellationToken cancellationToken = default)
    {
        var catalog = GXLocalizationJson.Read<GXLocalizationCatalog>(await reader.ReadAsync(source, cancellationToken), source);
        GXLocalizationJson.ValidateCatalog(catalog);
        foreach (var item in catalog.Items)
        {
            foreach (var release in item.Releases)
            {
                _ = reader.ResolveReference(source, release.Path);
            }
        }
        return catalog;
    }

    /// <summary>Reads and validates a complete JSON language package from a local path or HTTPS address.</summary>
    public async Task<GXLocalizationPackage> ReadPackageAsync(string source, CancellationToken cancellationToken = default)
    {
        var package = GXLocalizationJson.Read<GXLocalizationPackage>(await reader.ReadAsync(source, cancellationToken), source);
        GXLocalizationJson.ValidatePackage(package);
        return package;
    }

    /// <summary>Lists the newest stable release per owner, or all selected versions, with optional filters.</summary>
    public IReadOnlyList<GXLocalizationCatalogItem> List(GXLocalizationCatalog catalog, string? owner = null,
        string? ownerType = null, string? culture = null, bool allVersions = false, bool includePrereleases = false)
    {
        GXLocalizationJson.ValidateCatalog(catalog);
        if (ownerType != null && ownerType is not ("application" or "module"))
        {
            throw new ArgumentException("--owner-type must be application or module.", nameof(ownerType));
        }
        List<GXLocalizationCatalogItem> result = [];
        foreach (var item in catalog.Items.OrderBy(x => x.Owner, StringComparer.Ordinal))
        {
            if (owner != null && item.Owner != owner || ownerType != null && item.OwnerType != ownerType)
            {
                continue;
            }
            IEnumerable<GXLocalizationRelease> releases = item.Releases.Where(x => includePrereleases || !GXLocalizationJson.Version(x.Version).IsPrerelease)
                .OrderByDescending(x => GXLocalizationJson.Version(x.Version), VersionComparer.VersionRelease);
            if (!allVersions)
            {
                releases = releases.Take(1);
            }
            // A default listing describes the current package, not an older package with a removed culture.
            if (culture != null)
            {
                releases = releases.Where(x => x.Cultures.Contains(culture, StringComparer.OrdinalIgnoreCase));
            }
            List<GXLocalizationRelease> selected = releases.ToList();
            if (selected.Count != 0)
            {
                result.Add(new GXLocalizationCatalogItem { Owner = item.Owner, OwnerType = item.OwnerType, Description = item.Description, Releases = selected });
            }
        }
        return result;
    }

    /// <summary>Adds a package to a local checkout. Updates retain old versions; replace explicitly corrects an existing version.</summary>
    public async Task<GXLocalizationCatalog> AddAsync(string catalogPath, string packagePath, bool update = false,
        bool replace = false, CancellationToken cancellationToken = default)
    {
        if (reader.GetRemoteUri(catalogPath) != null || reader.GetRemoteUri(packagePath) != null)
        {
            throw new ArgumentException("Adding/updating requires a local catalog and a local JSON package. GitHub Pages is read-only; publish a checkout using commit, push and Actions.");
        }
        if (replace && !update)
        {
            throw new ArgumentException("--replace is only supported by localization update.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        catalogPath = Path.GetFullPath(catalogPath);
        string directory = Path.GetDirectoryName(catalogPath)!;
        Directory.CreateDirectory(directory);
        RejectLink(catalogPath);
        // A persistent lock file avoids delete/recreate races between concurrent writers.
        string lockPath = catalogPath + ".lock";
        RejectLink(lockPath);
        using FileStream catalogLock = AcquireLock(lockPath);
        byte[] bytes = await reader.ReadAsync(packagePath, cancellationToken);
        var package = GXLocalizationJson.Read<GXLocalizationPackage>(bytes, packagePath);
        GXLocalizationJson.ValidatePackage(package);
        GXLocalizationCatalog catalog = File.Exists(catalogPath)
            ? await GetCatalogAsync(catalogPath, cancellationToken) : new();
        var item = catalog.Items.SingleOrDefault(x => x.Owner == package.Owner);
        if (item == null && catalog.Items.Any(x => string.Equals(x.Owner, package.Owner, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Owner identifiers differing only in case would collide on Windows.");
        }
        if (update && item == null)
        {
            throw new InvalidOperationException($"Owner '{package.Owner}' does not exist. Use localization add first.");
        }
        if (item != null && item.OwnerType != package.OwnerType)
        {
            throw new InvalidOperationException($"Owner '{package.Owner}' has type '{item.OwnerType}', not '{package.OwnerType}'.");
        }
        NuGetVersion version = GXLocalizationJson.Version(package.Version);
        var previous = item?.Releases.SingleOrDefault(x => VersionComparer.VersionRelease.Equals(GXLocalizationJson.Version(x.Version), version));
        if (previous != null && !replace)
        {
            throw new InvalidOperationException($"Duplicate localization release '{package.Owner}/{package.Version}'. Publish a new version or use update --replace explicitly.");
        }
        if (replace && previous == null)
        {
            throw new InvalidOperationException("--replace requires an existing version. Use update without --replace to publish a new version.");
        }
        if (update && !replace && item!.Releases.Any(x => VersionComparer.VersionRelease.Compare(version, GXLocalizationJson.Version(x.Version)) <= 0))
        {
            throw new InvalidOperationException("An update must be greater than the newest existing package version, including prereleases.");
        }
        string relative = $"packages/{package.Owner}/{version.ToNormalizedString()}.json";
        string target = ManagedPath(directory, relative);
        if (string.Equals(target, catalogPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The catalog path collides with its managed package path.");
        }
        if (File.Exists(target) && (previous == null || !string.Equals(reader.ResolveReference(catalogPath, previous.Path), target, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Package file '{target}' already exists and will not be overwritten.");
        }
        string description = string.IsNullOrWhiteSpace(package.Description)
            ? $"Localized resources for {package.Owner} {package.OwnerType}" : package.Description;
        var release = new GXLocalizationRelease
        {
            Version = package.Version, OwnerVersionRange = package.OwnerVersionRange, DefaultCulture = package.DefaultCulture,
            Cultures = package.Resources.Keys.ToList(), Description = description, ReleaseNotes = package.ReleaseNotes,
            Path = relative, Size = bytes.LongLength, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
        };
        if (item == null)
        {
            item = new GXLocalizationCatalogItem { Owner = package.Owner, OwnerType = package.OwnerType, Description = description };
            catalog.Items.Add(item);
        }
        if (previous != null)
        {
            item.Releases.Remove(previous);
        }
        item.Releases.Add(release);
        catalog.GeneratedAt = DateTimeOffset.UtcNow;
        GXLocalizationJson.ValidateCatalog(catalog);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await WriteTransactionAsync(catalogPath, target, bytes, JsonSerializer.SerializeToUtf8Bytes(catalog, GXLocalizationJson.Options), cancellationToken);
        return catalog;
    }

    /// <summary>Copies/downloads a compatible package and verifies its size, SHA-256, JSON and catalog metadata before publishing the output.</summary>
    public async Task<string> DownloadAsync(string catalogSource, string owner, string outputDirectory,
        string? ownerVersion = null, string? version = null, bool includePrereleases = false,
        IProgress<GXUpdateProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(catalogSource, cancellationToken);
        var item = catalog.Items.SingleOrDefault(x => x.Owner == owner)
            ?? throw new KeyNotFoundException($"Localization owner '{owner}' was not found.");
        NuGetVersion? ownerValue = ownerVersion == null ? null : GXLocalizationJson.Version(ownerVersion);
        NuGetVersion? requested = version == null ? null : GXLocalizationJson.Version(version);
        var release = item.Releases.Where(x => (includePrereleases || !GXLocalizationJson.Version(x.Version).IsPrerelease) &&
                (requested == null || VersionComparer.VersionRelease.Equals(GXLocalizationJson.Version(x.Version), requested)) &&
                (ownerValue == null || x.OwnerVersionRange == null || VersionRange.Parse(x.OwnerVersionRange).Satisfies(ownerValue)))
            .OrderByDescending(x => GXLocalizationJson.Version(x.Version), VersionComparer.VersionRelease).FirstOrDefault()
            ?? throw new InvalidOperationException($"No compatible localization package for '{owner}' (owner version: {ownerVersion ?? "unspecified"}, package version: {version ?? "latest stable"}).");
        return await DownloadReleaseAsync(catalogSource, item, release, outputDirectory, progress, cancellationToken);
    }

    /// <summary>Updates a local JSON package or every JSON package inside a ZIP after validating the complete downloads.</summary>
    public async Task<bool> UpdatePackageAsync(string catalogSource, string packagePath, bool includePrereleases = false,
        IProgress<GXUpdateProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (reader.GetRemoteUri(packagePath) != null)
        {
            throw new ArgumentException("--localization must be an existing local JSON or ZIP package file.", nameof(packagePath));
        }
        string target = Path.GetFullPath(packagePath);
        RejectLink(target);
        if (string.Equals(Path.GetExtension(target), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return await UpdateArchiveAsync(catalogSource, target, includePrereleases, progress, cancellationToken);
        }
        var installed = await ReadPackageAsync(target, cancellationToken);
        var catalog = await GetCatalogAsync(catalogSource, cancellationToken);
        var item = catalog.Items.SingleOrDefault(x => x.Owner == installed.Owner);
        if (item == null)
        {
            throw new InvalidOperationException("Localization owner is not published. Use publish <localization.json> first.");
        }
        if (item.OwnerType != installed.OwnerType)
        {
            throw new InvalidDataException($"Localization owner type does not match the installed package '{installed.Owner}'.");
        }
        var release = List(catalog, owner: installed.Owner, includePrereleases: includePrereleases).FirstOrDefault()?.Releases[0]
            ?? throw new InvalidOperationException($"No matching localization release for '{installed.Owner}'.");
        if (VersionComparer.VersionRelease.Compare(GXLocalizationJson.Version(release.Version), GXLocalizationJson.Version(installed.Version)) <= 0)
        {
            return false;
        }
        string staging = Path.Combine(Path.GetDirectoryName(target)!, ".gurux-localization-" + Guid.NewGuid().ToString("N"));
        try
        {
            string downloaded = await DownloadReleaseAsync(catalogSource, item, release, staging, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            RejectLink(target);
            File.Move(downloaded, target, true);
            return true;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    private async Task<bool> UpdateArchiveAsync(string catalogSource, string target, bool includePrereleases,
        IProgress<GXUpdateProgress>? progress, CancellationToken cancellationToken)
    {
        string staging = Path.Combine(Path.GetDirectoryName(target)!, ".gurux-localization-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        string replacement = Path.Combine(staging, "updated.zip");
        bool updated = false;
        int packages = 0;
        try
        {
            using (var input = ZipFile.OpenRead(target))
            using (var output = ZipFile.Open(replacement, ZipArchiveMode.Create))
            {
                foreach (var entry in input.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string? package = null;
                    if (!entry.FullName.EndsWith('/') && entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        // Use an independent name so archive paths never become filesystem paths.
                        package = Path.Combine(staging, $"{packages++}.json");
                        await using (var source = entry.Open())
                        await using (var destination = File.Create(package))
                        {
                            await source.CopyToAsync(destination, cancellationToken);
                        }
                        updated |= await UpdatePackageAsync(catalogSource, package, includePrereleases, progress, cancellationToken);
                    }
                    var copied = output.CreateEntry(entry.FullName);
                    copied.LastWriteTime = entry.LastWriteTime;
                    copied.ExternalAttributes = entry.ExternalAttributes;
                    await using var content = copied.Open();
                    await using var original = package == null ? entry.Open() : File.OpenRead(package);
                    await original.CopyToAsync(content, cancellationToken);
                }
            }
            if (packages == 0) throw new InvalidDataException("The localization ZIP contains no JSON packages.");
            if (!updated) return false;
            cancellationToken.ThrowIfCancellationRequested();
            RejectLink(target);
            File.Move(replacement, target, true);
            return true;
        }
        finally
        {
            Directory.Delete(staging, true);
        }
    }

    private async Task<string> DownloadReleaseAsync(string catalogSource, GXLocalizationCatalogItem item,
        GXLocalizationRelease release, string outputDirectory, IProgress<GXUpdateProgress>? progress, CancellationToken cancellationToken)
    {
        string source = reader.ResolveReference(catalogSource, release.Path);
        string directory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);
        string target = ManagedPath(directory, $"{item.Owner}-{GXLocalizationJson.Version(release.Version).ToNormalizedString()}.json");
        if (reader.GetRemoteUri(source) == null && string.Equals(Path.GetFullPath(source), target, StringComparison.OrdinalIgnoreCase) ||
            reader.GetRemoteUri(catalogSource) == null && string.Equals(Path.GetFullPath(catalogSource), target, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The output must not overwrite the source package or catalog.");
        }
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await reader.CopyToAsync(source, output, progress, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            byte[] bytes = await File.ReadAllBytesAsync(temporary, cancellationToken);
            if (bytes.LongLength != release.Size || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Size or SHA-256 mismatch for localization package '{item.Owner}/{release.Version}'.");
            }
            GXLocalizationJson.MatchPackage(GXLocalizationJson.Read<GXLocalizationPackage>(bytes, source), item, release);
            cancellationToken.ThrowIfCancellationRequested();
            RejectLink(target);
            File.Move(temporary, target, true);
            return target;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>Validates every retained version and hash, for example before publishing a checkout to GitHub Pages.</summary>
    public async Task ValidateAllAsync(string catalogSource, CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(catalogSource, cancellationToken);
        string temporary = Path.Combine(Path.GetTempPath(), "Gurux.Localization.Validation", Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var item in catalog.Items)
            {
                foreach (var release in item.Releases)
                {
                    await DownloadReleaseAsync(catalogSource, item, release, temporary, null, cancellationToken);
                }
            }
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, true);
            }
        }
    }

    private static FileStream AcquireLock(string path)
    {
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new IOException("Cannot lock the localization catalog. Another writer may be updating it; retry after it finishes.", ex);
        }
    }

    private static string ManagedPath(string root, string relative)
    {
        string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        string relation = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relation) || relation == ".." || relation.StartsWith(".." + Path.DirectorySeparatorChar))
        {
            throw new InvalidDataException("Managed package path must stay inside its destination directory.");
        }
        string? current = path;
        while (current != null)
        {
            RejectLink(current);
            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
            current = Path.GetDirectoryName(current);
        }
        return path;
    }

    private static void RejectLink(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"Managed path '{path}' must not be a symbolic link or junction.");
        }
    }

    private static async Task WriteTransactionAsync(string catalog, string target, byte[] packageBytes, byte[] catalogBytes, CancellationToken token)
    {
        string suffix = "." + Guid.NewGuid().ToString("N");
        string packageTemp = target + suffix + ".tmp";
        string catalogTemp = catalog + suffix + ".tmp";
        string backup = target + suffix + ".bak";
        bool installed = false;
        bool committed = false;
        bool hadTarget = File.Exists(target);
        try
        {
            await StageAsync(packageTemp, packageBytes, token);
            await StageAsync(catalogTemp, catalogBytes, token);
            token.ThrowIfCancellationRequested();
            if (hadTarget)
            {
                File.Replace(packageTemp, target, backup);
            }
            else
            {
                File.Move(packageTemp, target);
            }
            installed = true;
            File.Move(catalogTemp, catalog, true);
            committed = true;
        }
        catch
        {
            if (installed)
            {
                if (hadTarget)
                {
                    File.Move(backup, target, true);
                }
                else
                {
                    File.Delete(target);
                }
            }
            throw;
        }
        finally
        {
            File.Delete(packageTemp);
            File.Delete(catalogTemp);
            if (committed)
            {
                File.Delete(backup);
            }
        }
    }

    private static async Task StageAsync(string path, byte[] bytes, CancellationToken token)
    {
        await using FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        await file.WriteAsync(bytes, token);
        await file.FlushAsync(token);
        file.Flush(flushToDisk: true);
    }
}
