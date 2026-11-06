// --------------------------------------------------------------------------
// Gurux Ltd
//
// Copyright (c) Gurux Ltd.
//
// This file is part of Gurux Device Framework.
//
// Commit: $Format:%H$
// Date:   $Format:%cI$
//
// This software is available under either of the following licenses:
//
// 1. GNU General Public License, version 2 only (GPL-2.0-only).
//    See the LICENSE file included with this distribution.
//
// 2. A commercial license granted by Gurux Ltd under a separate written
//    agreement. Use under this option requires a valid commercial license
//    and is governed by the terms of that agreement.
//
// If you have not obtained a commercial license from Gurux Ltd,
// use of this software is governed by GPL-2.0-only.
//
// Commercial licensing: https://www.gurux.fi/
//
// SPDX-License-Identifier: GPL-2.0-only OR LicenseRef-Gurux-Commercial
// --------------------------------------------------------------------------

using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Gurux.Updater.Model;
using NuGet.Versioning;

namespace Gurux.Updater.Services;

/// <summary>Stages immutable Linux releases and activates them with an atomic symbolic-link rename.</summary>
public sealed class GXDeploymentService(HttpClient client, IGXServiceManager? services = null)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [DllImport("libc", SetLastError = true, EntryPoint = "rename")]
    static extern int Rename(string source, string destination);

    /// <summary>Validates and normalizes server-owned installation settings.</summary>
    public static void ValidateOptions(GXDeploymentOptions options)
    {
        if (!Path.IsPathFullyQualified(options.Root) || Path.TrimEndingDirectorySeparator(options.Root) == Path.GetPathRoot(options.Root))
        {
            throw new ArgumentException("Deployment root must be an absolute non-root directory.");
        }
        options.Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Root));
        _ = HttpUri(options.PublicUrl);
        _ = HttpUri(options.CatalogUrl);
        if (options.CheckTimeoutSeconds < 1 || options.MaxPackageBytes < 1 || options.MaxExtractedBytes < 1)
        {
            throw new ArgumentException("Invalid deployment size or timeout limits.");
        }
        if (options.IncludeBackend)
        {
            if (options.ServiceName.Length == 0 || options.ServiceName.StartsWith('-') || options.ServiceName.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-' and not '@'))
            {
                throw new ArgumentException("Invalid systemd service name.");
            }
            if (Path.GetFileName(options.BackendAssembly) != options.BackendAssembly || !options.BackendAssembly.EndsWith(".dll", StringComparison.Ordinal))
            {
                throw new ArgumentException("BackendAssembly must be a DLL basename.");
            }
            _ = HttpUri(options.ApiVersionUrl ?? throw new ArgumentException("Backend API version URL is required."));
        }
    }
    static Uri HttpUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) || uri.UserInfo.Length != 0)
        {
            throw new ArgumentException("Use HTTPS (or HTTP loopback for local deployments).");
        }
        return uri;
    }
    /// <summary>Resolves a requested version from a trusted catalog and enforces standalone API compatibility.</summary>
    public static GXUpdateRelease SelectRelease(GXUpdateCatalog catalog, GXDeploymentOptions options, string version)
    {
        GXDeploymentQueue.ValidateVersion(version);
        var item = catalog.Items.SingleOrDefault(i => i.Id == options.ProductId)
            ?? throw new InvalidDataException("Deployment product is absent from the trusted catalog.");
        var release = item.Releases.SingleOrDefault(r => r.Version == version && !r.IsRevoked && !r.IsUnsupported && !r.IsPrerelease)
            ?? throw new InvalidDataException("Requested stable release is unavailable.");
        if (!options.IncludeBackend && (release.AmiVersionRange is null || !NuGetVersion.TryParse(options.ApiVersion, out var host) || !VersionRange.Parse(release.AmiVersionRange).Satisfies(host)))
        {
            throw new InvalidDataException("The WASM release is not declared compatible with the running API.");
        }
        _ = Asset(release);
        return release;
    }
    static GXUpdateAsset Asset(GXUpdateRelease release)
    {
        var assets = release.Assets.Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (assets.Length != 1)
        {
            throw new InvalidDataException("Deployment release must select exactly one ZIP asset.");
        }
        var asset = assets[0];
        _ = HttpUri(asset.DownloadUrl);
        _ = Digest(asset);
        return asset;
    }
    static byte[] Digest(GXUpdateAsset asset)
    {
        string value = asset.Digest ?? "";
        if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            value = value[7..];
        }
        if (value.Length != 64 || !value.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("A SHA-256 catalog digest is required.");
        }
        return Convert.FromHexString(value);
    }
    /// <summary>Downloads and verifies before creating the final immutable release directory.</summary>
    public async Task<string> StageAsync(GXDeploymentOptions options, GXUpdateRelease release, CancellationToken token = default)
    {
        ValidateOptions(options);
        GXDeploymentQueue.ValidateVersion(release.Version);
        var asset = Asset(release);
        string releases = Path.Combine(options.Root, "releases");
        Directory.CreateDirectory(releases);
        if (new DirectoryInfo(releases).LinkTarget is not null)
        {
            throw new IOException("releases must not be a symlink.");
        }
        string final = Path.Combine(releases, release.Version);
        string stage = Path.Combine(releases, ".stage-" + Guid.NewGuid().ToString("N"));
        string package = Path.Combine(options.Root, ".download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, HttpUri(asset.DownloadUrl));
            if (!string.IsNullOrEmpty(options.GitHubTokenEnvironmentVariable))
            {
                var secret = Environment.GetEnvironmentVariable(options.GitHubTokenEnvironmentVariable);
                if (string.IsNullOrEmpty(secret))
                {
                    throw new InvalidOperationException("Configured GitHub token environment variable is missing.");
                }
                // Never send GitHub credentials to arbitrary catalog hosts.
                if (request.RequestUri!.Host is not ("github.com" or "api.github.com"))
                {
                    throw new InvalidDataException("Private downloads must use a GitHub host.");
                }
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
                request.Headers.Accept.ParseAdd("application/octet-stream");
            }
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > options.MaxPackageBytes)
            {
                throw new InvalidDataException("Package too large.");
            }
            await using (var output = new FileStream(package, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            {
                byte[] buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) != 0)
                { total += read; if (total > options.MaxPackageBytes)
                {
                    throw new InvalidDataException("Package too large.");
                } await output.WriteAsync(buffer.AsMemory(0, read), token); }
            }
            await using (var file = File.OpenRead(package))
                if (!CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(file, token), Digest(asset)))
                {
                    throw new InvalidDataException("Package SHA-256 mismatch.");
                }
            using (var zip = ZipFile.OpenRead(package))
            {
                long total = 0;
                if (zip.Entries.Count > 100000)
                {
                    throw new InvalidDataException("Too many ZIP entries.");
                }
                foreach (var entry in zip.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    string name = entry.FullName;
                    int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
                    if (name.Length == 0 || name.Contains('\\') || name.Contains(':') || name.StartsWith('/') || name.Split('/').Any(p => p == "..") || (unixType != 0 && unixType != 0x8000 && unixType != 0x4000))
                    {
                        throw new InvalidDataException("Unsafe ZIP entry.");
                    }
                    string destination = Path.GetFullPath(Path.Combine(stage, name));
                    if (!destination.StartsWith(stage + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("ZIP entry escapes its release.");
                    }
                    if (entry.Length > options.MaxExtractedBytes - total)
                    {
                        throw new InvalidDataException("Extracted package too large.");
                    }
                    total += entry.Length;
                    if (name.EndsWith('/'))
                    { Directory.CreateDirectory(destination); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await using var source = entry.Open();
                    await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    await source.CopyToAsync(target, token);
                }
            }
            ValidateLayout(stage, options, release.Version);
            if (OperatingSystem.IsLinux())
            {
                // The agent journal uses a restrictive umask; public release files must still be readable by nginx.
                const UnixFileMode directoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
                const UnixFileMode fileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
                File.SetUnixFileMode(stage, directoryMode);
                foreach (var subdirectory in Directory.EnumerateDirectories(stage, "*", SearchOption.AllDirectories))
                {
                    File.SetUnixFileMode(subdirectory, directoryMode);
                }
                foreach (var file in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories))
                {
                    File.SetUnixFileMode(file, fileMode);
                }
            }
            await File.WriteAllTextAsync(Path.Combine(stage, ".package-sha256"), Convert.ToHexString(Digest(asset)), token);
            if (Directory.Exists(final))
            {
                // Reuse only a completed release produced from the same immutable package.
                if (new DirectoryInfo(final).LinkTarget is not null || !File.Exists(Path.Combine(final, ".package-sha256")) ||
                    await File.ReadAllTextAsync(Path.Combine(final, ".package-sha256"), token) != Convert.ToHexString(Digest(asset)))
                {
                    throw new IOException("Release directory exists with different or unknown content.");
                }
                ValidateLayout(final, options, release.Version);
            }
            else
            {
                Directory.Move(stage, final);
            }
            return final;
        }
        finally
        {
            File.Delete(package);
            if (Directory.Exists(stage))
            {
                Directory.Delete(stage, true);
            }
        }
    }
    static string WebRoot(string release, GXDeploymentOptions options) => options.IncludeBackend
        ? Path.Combine(release, Directory.Exists(Path.Combine(release, "standalone")) ? "standalone" : "wwwroot") : release;
    static string[] BootstrapFiles(string web)
    {
        // .NET 10 embeds the boot manifest in dotnet.js instead of blazor.boot.json.
        List<string> files = ["_framework/blazor.webassembly.js"];
        var boot = Path.Combine(web, "_framework", "blazor.boot.json");
        JsonDocument manifest;
        bool embedded = !File.Exists(boot);
        if (!embedded)
        {
            files.Add("_framework/blazor.boot.json");
            manifest = JsonDocument.Parse(File.ReadAllText(boot));
        }
        else
        {
            string runtime = Path.Combine(web, "_framework", "dotnet.js");
            if (!File.Exists(runtime))
            {
                throw new InvalidDataException("Blazor boot manifest/runtime is missing.");
            }
            files.Add("_framework/dotnet.js");
            string script = File.ReadAllText(runtime);
            int start = script.IndexOf("/*json-start*/", StringComparison.Ordinal);
            int end = start < 0 ? -1 : script.IndexOf("/*json-end*/", start, StringComparison.Ordinal);
            if (start < 0 || end < 0)
            {
                throw new InvalidDataException("Embedded Blazor boot manifest is missing.");
            }
            start += "/*json-start*/".Length;
            manifest = JsonDocument.Parse(script[start..end]);
        }
        using (manifest)
        {
            if (manifest.RootElement.TryGetProperty("resources", out var resources))
            {
                // Culture satellites are loaded on demand; verify all initial runtime/assembly resources.
                foreach (string group in new[] { "runtime", "runtimeAssets", "jsModuleNative", "jsModuleRuntime", "wasmNative", "icu", "coreAssembly", "assembly", "lazyAssembly" })
                {
                    if (!resources.TryGetProperty(group, out var values))
                    {
                        continue;
                    }
                    IEnumerable<string?> names = values.ValueKind == JsonValueKind.Array
                        ? values.EnumerateArray().Select(item => item.GetProperty("name").GetString())
                        : values.ValueKind == JsonValueKind.Object ? values.EnumerateObject().Select(item => (string?)item.Name) : [];
                    foreach (var name in names)
                    {
                        if (string.IsNullOrEmpty(name) || name.Contains('\\') || name.Contains(':') || name.StartsWith('/') || name.Split('/').Any(p => p is ".." or "."))
                        {
                            throw new InvalidDataException("Unsafe Blazor boot resource path.");
                        }
                        files.Add("_framework/" + name);
                    }
                }
            }
            if (embedded)
            {
                string? main = manifest.RootElement.GetProperty("mainAssemblyName").GetString();
                if (string.IsNullOrEmpty(main) || !files.Any(f => f.StartsWith("_framework/" + main + ".", StringComparison.Ordinal)) ||
                    !files.Any(f => f.StartsWith("_framework/dotnet.native.", StringComparison.Ordinal) && f.EndsWith(".wasm", StringComparison.Ordinal)))
                {
                    throw new InvalidDataException("Blazor boot manifest is missing the application or native runtime.");
                }
            }
        }
        return files.Distinct(StringComparer.Ordinal).ToArray();
    }
    static void ValidateLayout(string directory, GXDeploymentOptions options, string version)
    {
        if (options.IncludeBackend)
        {
            foreach (string name in new[] { options.BackendAssembly, Path.GetFileNameWithoutExtension(options.BackendAssembly) + ".deps.json", Path.GetFileNameWithoutExtension(options.BackendAssembly) + ".runtimeconfig.json" })
                {
                    if (!File.Exists(Path.Combine(directory, name)))
                        {
                            throw new InvalidDataException("Missing published backend file: " + name);
                        }
                }
        }
        string web = WebRoot(directory, options);
        foreach (string file in new[] { "index.html", "version.json" }.Concat(BootstrapFiles(web)))
        {
            if (!File.Exists(Path.Combine(web, file)))
                {
                    throw new InvalidDataException("Missing WASM startup file: " + file);
                }
        }
        using var marker = JsonDocument.Parse(File.ReadAllText(Path.Combine(web, "version.json")));
        if (marker.RootElement.GetProperty("version").GetString() != version)
        {
            throw new InvalidDataException("WASM version does not match the catalog.");
        }
    }
    static string? Current(GXDeploymentOptions options)
    {
        string current = Path.Combine(options.Root, "current");
        string? target = new DirectoryInfo(current).LinkTarget;
        if (target is null && Directory.Exists(current))
        {
            throw new IOException("current must be a symbolic link.");
        }
        if (target is not null)
        {
            string absolute = Path.GetFullPath(Path.Combine(options.Root, target));
            if (!absolute.StartsWith(Path.Combine(options.Root, "releases") + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new IOException("current must point inside releases.");
            }
        }
        return target;
    }
    static void Switch(GXDeploymentOptions options, string? target)
    {
        string current = Path.Combine(options.Root, "current");
        if (target is null)
        { File.Delete(current); return; }
        string resolved = Path.GetFullPath(Path.Combine(options.Root, target));
        string releases = Path.Combine(options.Root, "releases");
        if (Path.GetDirectoryName(resolved) != releases || !Directory.Exists(resolved) || new DirectoryInfo(resolved).LinkTarget is not null)
        {
            throw new IOException("Activation target must be a real release directory inside releases.");
        }
        string temporary = Path.Combine(options.Root, ".current-" + Guid.NewGuid().ToString("N"));
        Directory.CreateSymbolicLink(temporary, target);
        try
        {
            if (Rename(temporary, current) != 0)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
        finally { if (new DirectoryInfo(temporary).LinkTarget is not null)
        {
            File.Delete(temporary);
        } }
    }
    /// <summary>Requires an independent process when the backend service is being replaced.</summary>
    public async Task InstallAsync(GXDeploymentOptions options, GXUpdateRelease release, CancellationToken token = default, string? requestId = null)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Atomic deployment requires Linux.");
        }
        ValidateOptions(options);
        Directory.CreateDirectory(options.Root);
        using var gate = new FileStream(Path.Combine(options.Root, "deployment.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var queue = new GXDeploymentQueue(options.Root);
        var status = new GXDeploymentStatus { RequestId = requestId, Version = release.Version, State = "Downloading", PreviousTarget = Current(options) };
        await queue.WriteStatusAsync(status, token);
        bool activating = false;
        try
        {
            string directory = await StageAsync(options, release, token);
            if (Path.GetFullPath(Path.Combine(options.Root, Current(options) ?? ".")) == directory)
            {
                await CheckAsync(options, directory, release.Version, token);
                status.State = "Succeeded";
                await queue.WriteStatusAsync(status, token);
                return;
            }
            // Persist rollback information BEFORE stopping the service or switching the link.
            status.State = "Activating";
            await queue.WriteStatusAsync(status, token);
            activating = true;
            var manager = services ?? new GXSystemdServiceManager();
            if (options.IncludeBackend)
            {
                await manager.StopAsync(options.ServiceName, token);
            }
            Switch(options, Path.GetRelativePath(options.Root, directory));
            if (options.IncludeBackend)
            {
                await manager.StartAsync(options.ServiceName, token);
            }
            status.State = "Verifying";
            await queue.WriteStatusAsync(status, token);
            await CheckAsync(options, directory, release.Version, token);
            status.State = "Succeeded";
            await queue.WriteStatusAsync(status, token);
        }
        catch (Exception error)
        {
            status.Error = error.Message;
            status.State = activating ? "RollingBack" : "Failed";
            Exception? journalError = null;
            try
            { await queue.WriteStatusAsync(status); }
            catch (Exception persistence) { journalError = persistence; }
            if (activating)
            {
                try
                { await RestoreAsync(options, status); status.State = "RolledBack"; }
                catch (Exception rollback) { status.State = "RollbackFailed"; status.Error += " Rollback: " + rollback.Message; }
                try
                { await queue.WriteStatusAsync(status); }
                catch (Exception persistence) { journalError = persistence; }
            }
            if (journalError is not null)
            {
                throw new AggregateException("Deployment failed; the outcome could not be persisted. " + status.State, error, journalError);
            }
            throw;
        }
    }
    /// <summary>Restores the retained previous successful release without downloading an arbitrary older version.</summary>
    public async Task RestorePreviousAsync(GXDeploymentOptions options, GXDeploymentRequest request, CancellationToken token = default)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Atomic deployment requires Linux.");
        }
        ValidateOptions(options);
        if (!request.RestorePrevious || !Guid.TryParseExact(request.RequestId, "N", out _))
        {
            throw new ArgumentException("An explicit restore transaction is required.");
        }
        using var gate = new FileStream(Path.Combine(options.Root, "deployment.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string current = Current(options) ?? throw new InvalidDataException("No active installation.");
        string currentVersion = Path.GetFileName(Path.GetFullPath(Path.Combine(options.Root, current)));
        var previous = await GXDeploymentRestoration.ReadPreviousAsync(options.Root, currentVersion, false, token);
        if (previous is null || request.Version != GXDeploymentRestoration.PreviousVersion(previous))
        {
            throw new InvalidDataException("Only the retained previous successful installation can be restored.");
        }
        string directory = Path.GetFullPath(Path.Combine(options.Root, previous.PreviousTarget!));
        ValidateLayout(directory, options, request.Version);
        await CheckAsync(options, Path.GetFullPath(Path.Combine(options.Root, current)), currentVersion, token);
        var status = new GXDeploymentStatus { RequestId = request.RequestId, Version = request.Version, RestorePrevious = true, PreviousTarget = current, State = "Activating" };
        var queue = new GXDeploymentQueue(options.Root);
        await queue.WriteStatusAsync(status, token);
        try
        {
            await RestoreAsync(options, previous);
            status.State = "Verifying";
            await queue.WriteStatusAsync(status);
            await CheckAsync(options, directory, request.Version, token);
            status.State = "Succeeded";
            await queue.WriteStatusAsync(status);
        }
        catch (Exception error)
        {
            status.Error = error.Message; status.State = "RollingBack";
            Exception? persistence = null;
            try { await queue.WriteStatusAsync(status); } catch (Exception ex) { persistence = ex; }
            try
            {
                await RestoreAsync(options, status);
                await CheckAsync(options, Path.GetFullPath(Path.Combine(options.Root, current)), currentVersion, CancellationToken.None);
                status.State = "RolledBack";
            }
            catch (Exception ex) { status.State = "RollbackFailed"; status.Error += " Restore: " + ex.Message; }
            try { await queue.WriteStatusAsync(status); } catch (Exception ex) { persistence = ex; }
            if (persistence is not null)
            {
                throw new AggregateException("Restore outcome could not be persisted.", error, persistence);
            }
            throw;
        }
    }
    async Task RestoreAsync(GXDeploymentOptions options, GXDeploymentStatus status)
    {
        var manager = services ?? new GXSystemdServiceManager();
        if (options.IncludeBackend)
        {
            await manager.StopAsync(options.ServiceName);
        }
        Switch(options, status.PreviousTarget);
        if (options.IncludeBackend && status.PreviousTarget is not null)
        {
            await manager.StartAsync(options.ServiceName);
        }
        if (status.PreviousTarget is null)
        {
            throw new InvalidDataException("No previous installation is available for public rollback verification.");
        }
        string restored = Path.GetFullPath(Path.Combine(options.Root, status.PreviousTarget));
        using var marker = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(WebRoot(restored, options), "version.json")));
        string version = marker.RootElement.GetProperty("version").GetString() ?? throw new InvalidDataException("Restored installation has no version.");
        GXDeploymentQueue.ValidateVersion(version);
        await CheckAsync(options, restored, version, CancellationToken.None);
    }
    /// <summary>Recovers the persisted transaction after updater or host interruption.</summary>
    public async Task RecoverAsync(GXDeploymentOptions options)
    {
        ValidateOptions(options);
        Directory.CreateDirectory(options.Root);
        using var gate = new FileStream(Path.Combine(options.Root, "deployment.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var queue = new GXDeploymentQueue(options.Root);
        var status = await queue.ReadStatusAsync();
        if (status?.State is "Activating" or "Verifying" or "RollingBack" or "RollbackFailed")
        {
            try { await RestoreAsync(options, status); }
            catch (Exception error)
            {
                status.State = "RollbackFailed";
                status.Error = "Interrupted deployment restoration failed: " + error.Message;
                await queue.WriteStatusAsync(status);
                throw;
            }
            status.State = "RolledBack";
            status.Error = "Interrupted deployment recovered to previous release.";
            await queue.WriteStatusAsync(status);
            if (status.RequestId is not null && (await queue.ReadRequestAsync())?.RequestId == status.RequestId)
            {
                queue.CompleteRequest();
            }
        }
        else if (status?.State is "Succeeded" or "RolledBack" or "Failed")
        {
            await queue.WriteStatusAsync(status);
            // A crash may occur after committing the result but before acknowledging the request.
            if (status.RequestId is not null && (await queue.ReadRequestAsync())?.RequestId == status.RequestId)
            {
                queue.CompleteRequest();
            }
        }
    }
    /// <summary>Verifies the public boot resources against a WASM tree copied from the deployed container.</summary>
    public async Task VerifyPublicWasmAsync(string webRoot, string publicUrl, string version, int timeoutSeconds, CancellationToken token = default)
    {
        var options = new GXDeploymentOptions { PublicUrl = publicUrl, CheckTimeoutSeconds = timeoutSeconds };
        ValidateLayout(webRoot, options, version);
        using var response = await client.GetAsync(publicUrl, token); response.EnsureSuccessStatusCode();
        string html = await response.Content.ReadAsStringAsync(token);
        if (!html.Contains("blazor.web", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Public AMI page is missing its Blazor startup script.");
        }
        await CheckAsync(options, webRoot, version, token, checkIndex: false);
    }
    async Task CheckAsync(GXDeploymentOptions options, string directory, string version, CancellationToken token, bool checkIndex = true)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.CheckTimeoutSeconds));
        string web = WebRoot(directory, options);
        var baseUri = new Uri(options.PublicUrl.TrimEnd('/') + "/");
        Exception? last = null;
        try
        {
            while (true)
            {
                try
                {
                    foreach (string path in (checkIndex ? new[] { "version.json", "index.html" } : ["version.json"]).Concat(BootstrapFiles(web)))
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, path + "?gxupdate=" + Guid.NewGuid().ToString("N")));
                        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
                        using var response = await client.SendAsync(request, timeout.Token);
                        response.EnsureSuccessStatusCode();
                        byte[] received = await response.Content.ReadAsByteArrayAsync(timeout.Token);
                        byte[] expected = await File.ReadAllBytesAsync(Path.Combine(web, path), timeout.Token);
                        if (!SHA256.HashData(received).AsSpan().SequenceEqual(SHA256.HashData(expected)))
                        {
                            throw new InvalidDataException("Public server returned stale or incorrect startup resource: " + path);
                        }
                    }
                    if (options.IncludeBackend)
                    {
                        string actual = await client.GetStringAsync(options.ApiVersionUrl + (options.ApiVersionUrl!.Contains('?') ? "&" : "?") + "gxupdate=" + Guid.NewGuid().ToString("N"), timeout.Token);
                        if (actual.Trim().Trim('"') != version)
                        {
                            throw new InvalidDataException("Backend version differs from deployed release.");
                        }
                    }
                    return;
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidDataException) { last = ex; }
                await Task.Delay(500, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Public deployment verification failed: " + last?.Message, last); }
    }
}
