using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gurux.Updater.Model;
using NuGet.Versioning;

namespace Gurux.Updater.Services;

/// <summary>Publishes, downloads and restores standalone C# scripts without executing them.</summary>
public sealed class GXScriptService
{
    private readonly HttpClient client;
    private readonly GXCatalogSourceReader reader;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    /// <summary>Creates a service; plain HTTP is restricted to loopback development servers.</summary>
    public GXScriptService(HttpClient client)
    {
        this.client = client;
        reader = new GXCatalogSourceReader(client, allowLoopbackHttp: true);
    }
    /// <summary>Validates the metadata shared by publication and download.</summary>
    public static void Validate(GXScriptManifest m)
    {
        if (m.SchemaVersion != 1 || !Regex.IsMatch(m.Id ?? "", @"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,199}\z") ||
            new[] { m.Id, m.Version, m.FileName, m.Description, m.Author, m.License, m.AmiVersionRange, m.Sha256 }.Any(value => value?.Length > 4000) ||
            m.FileName?.Length > 200 || !NuGetVersion.TryParse(m.Version, out _) ||
            !Regex.IsMatch(m.FileName ?? "", @"\A[A-Za-z0-9_][A-Za-z0-9_.-]*\.(cs|csx)\z", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(m.FileName ?? "", @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\.", RegexOptions.IgnoreCase) ||
            string.IsNullOrWhiteSpace(m.Description) || string.IsNullOrWhiteSpace(m.Author) || string.IsNullOrWhiteSpace(m.License) ||
            string.IsNullOrWhiteSpace(m.AmiVersionRange) || !VersionRange.TryParse(m.AmiVersionRange, out _) ||
            !Regex.IsMatch(m.Sha256 ?? "", @"\A[0-9a-fA-F]{64}\z") || m.Size is <= 0 or > 4 * 1024 * 1024)
            throw new InvalidDataException("Invalid script manifest: require identity, version, .cs/.csx basename, description, author, license, AMI range, SHA-256 and size (1 byte–4 MiB).");
    }
    private static T Read<T>(byte[] bytes) => JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidDataException("Empty script JSON.");
    private static void Verify(GXScriptManifest m, byte[] bytes)
    {
        Validate(m);
        if (bytes.LongLength != m.Size || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(m.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Script size or SHA-256 mismatch.");
        try { if (new UTF8Encoding(false, true).GetString(bytes).Contains('\0')) throw new InvalidDataException("Script contains NUL bytes."); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("Script must be UTF-8 text.", ex); }
    }
    /// <summary>Reads a standalone JSON manifest.</summary>
    public async Task<GXScriptManifest> ReadManifestAsync(string path, CancellationToken token = default)
    {
        var manifest = Read<GXScriptManifest>(await File.ReadAllBytesAsync(path, token));
        Validate(manifest);
        return manifest;
    }
    /// <summary>Reads and validates all retained releases.</summary>
    public async Task<GXScriptCatalog> GetCatalogAsync(string source, CancellationToken token = default)
    {
        var catalog = Read<GXScriptCatalog>(await reader.ReadAsync(source, token));
        if (catalog.SchemaVersion != 1 || catalog.Items == null) throw new InvalidDataException("Unsupported script index.");
        HashSet<string> identities = new(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalog.Items)
        {
            Validate(item);
            if (string.IsNullOrWhiteSpace(item.Path) || !identities.Add(item.Id + "/" + NuGetVersion.Parse(item.Version).ToNormalizedString()))
                throw new InvalidDataException("Missing source path or duplicate script release.");
            _ = reader.ResolveReference(source, item.Path);
        }
        return catalog;
    }
    /// <summary>Selects the newest stable release unless a version or prerelease inclusion is requested.</summary>
    public GXScriptManifest Select(GXScriptCatalog catalog, string id, string? version = null, bool includePrereleases = false)
        => catalog.Items.Where(m => m.Id == id && (version != null
                ? NuGetVersion.Parse(m.Version) == NuGetVersion.Parse(version)
                : includePrereleases || !NuGetVersion.Parse(m.Version).IsPrerelease))
            .OrderByDescending(m => NuGetVersion.Parse(m.Version)).FirstOrDefault()
            ?? throw new InvalidDataException($"No matching release for script '{id}'.");
    /// <summary>Publishes verified local source with its separate manifest.</summary>
    public async Task PublishAsync(Uri index, string manifestPath, string scriptPath, CancellationToken token = default)
    {
        if (reader.GetRemoteUri(index.AbsoluteUri) == null) throw new ArgumentException("An HTTP(S) index is required.");
        var manifest = await ReadManifestAsync(manifestPath, token);
        if (!Path.GetFileName(scriptPath).Equals(manifest.FileName, StringComparison.Ordinal)) throw new InvalidDataException("Source basename differs from manifest fileName.");
        var bytes = await File.ReadAllBytesAsync(scriptPath, token);
        Verify(manifest, bytes);
        using var body = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new { manifest, content = Convert.ToBase64String(bytes) }, Json));
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await client.PostAsync(index, body, token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Script publication failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync(token)}");
    }
    /// <summary>Downloads a verified script and provenance. Existing files are never replaced by this operation.</summary>
    public async Task InstallAsync(string source, GXScriptManifest manifest, string destination, string amiVersion, CancellationToken token = default)
    {
        ValidateCompatibility(manifest, amiVersion);
        var path = Path.GetFullPath(destination);
        if (File.Exists(path) || File.Exists(path + ".gurux.json")) throw new IOException("Script already exists; use update explicitly.");
        var bytes = await reader.ReadAsync(reader.ResolveReference(source, manifest.Path), token);
        Verify(manifest, bytes);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string retainedSource = reader.GetRemoteUri(source) == null ? Path.GetFullPath(source) : source;
        var state = new GXInstalledScript { Manifest = manifest, Source = retainedSource, Original = bytes };
        // Publish both files without replacing anything created while the download was pending.
        bool created = false;
        try
        {
            await AtomicWriteAsync(path, bytes, token, overwrite: false);
            created = true;
            await AtomicWriteAsync(path + ".gurux.json", JsonSerializer.SerializeToUtf8Bytes(state, Json), token, overwrite: false);
        }
        catch
        {
            if (created) File.Delete(path);
            throw;
        }
    }
    private static void ValidateCompatibility(GXScriptManifest manifest, string amiVersion)
    {
        Validate(manifest);
        if (!NuGetVersion.TryParse(amiVersion, out var version) || !VersionRange.Parse(manifest.AmiVersionRange).Satisfies(version))
            throw new InvalidDataException("Script is incompatible with the supplied AMI version.");
    }
    /// <summary>Loads installed provenance and verifies the retained original source.</summary>
    public async Task<GXInstalledScript> GetInstalledAsync(string path, CancellationToken token = default)
    {
        var state = Read<GXInstalledScript>(await File.ReadAllBytesAsync(path + ".gurux.json", token));
        Verify(state.Manifest, state.Original);
        return state;
    }
    /// <summary>Detects local changes without needing a network connection.</summary>
    public async Task<bool> IsModifiedAsync(string path, CancellationToken token = default)
    {
        var state = await GetInstalledAsync(path, token);
        return !File.Exists(path) || !(await File.ReadAllBytesAsync(path, token)).AsSpan().SequenceEqual(state.Original);
    }
    /// <summary>Restores the installed original and returns the backup path, or null if unchanged.</summary>
    public async Task<string?> RejectChangesAsync(string path, CancellationToken token = default)
    {
        var state = await GetInstalledAsync(path, token);
        if (!await IsModifiedAsync(path, token)) return null;
        string? backup = Backup(path);
        await AtomicWriteAsync(path, state.Original, token);
        return backup;
    }
    /// <summary>Updates only to a newer catalog version. Local edits require explicit discard and are backed up.</summary>
    public async Task<bool> UpdateAsync(string path, string amiVersion, bool discardLocalChanges = false, bool includePrereleases = false, CancellationToken token = default)
    {
        var state = await GetInstalledAsync(path, token);
        var release = Select(await GetCatalogAsync(state.Source, token), state.Manifest.Id, includePrereleases: includePrereleases);
        if (NuGetVersion.Parse(release.Version) <= NuGetVersion.Parse(state.Manifest.Version)) return false;
        ValidateCompatibility(release, amiVersion);
        if (await IsModifiedAsync(path, token) && !discardLocalChanges) throw new InvalidOperationException("Script has local changes. Reject changes first or explicitly discard them.");
        var bytes = await reader.ReadAsync(reader.ResolveReference(state.Source, release.Path), token);
        Verify(release, bytes);
        Backup(path);
        await WriteInstallationAsync(path, bytes, new GXInstalledScript { Manifest = release, Source = state.Source, Original = bytes }, token);
        return true;
    }
    private static string? Backup(string path)
    {
        if (!File.Exists(path)) return null;
        var backup = path + "." + Guid.NewGuid().ToString("N") + ".bak";
        File.Copy(path, backup, overwrite: false);
        return backup;
    }
    private static async Task WriteInstallationAsync(string path, byte[] bytes, GXInstalledScript state, CancellationToken token)
    {
        byte[]? old = File.Exists(path) ? await File.ReadAllBytesAsync(path, token) : null;
        await AtomicWriteAsync(path, bytes, token);
        try { await AtomicWriteAsync(path + ".gurux.json", JsonSerializer.SerializeToUtf8Bytes(state, Json), token); }
        catch
        {
            if (old == null) File.Delete(path);
            else await AtomicWriteAsync(path, old, CancellationToken.None);
            throw;
        }
    }
    private static async Task AtomicWriteAsync(string path, byte[] bytes, CancellationToken token, bool overwrite = true)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: overwrite);
        }
        finally { File.Delete(temporary); }
    }
}
