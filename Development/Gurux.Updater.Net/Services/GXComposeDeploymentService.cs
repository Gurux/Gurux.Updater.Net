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

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gurux.Updater.Model;
using NuGet.Versioning;

namespace Gurux.Updater.Services;

/// <summary>Host-side, recoverable replacement of configured Compose services only.</summary>
public sealed class GXComposeDeploymentService(HttpClient client, IGXComposeRunner runner)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    /// <summary>Validates the host-owned project, services and endpoint settings.</summary>
    public static void ValidateOptions(GXDeploymentOptions options)
    {
        if (!Path.IsPathFullyQualified(options.Root) || Path.GetFullPath(options.Root) == Path.GetPathRoot(options.Root))
            throw new ArgumentException("An absolute installation root is required.");
        options.Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Root));
        var compose = options.Compose ?? throw new ArgumentException("Compose project settings are required.");
        if (compose.Channel is not ("stable" or "preview") || !Regex.IsMatch(compose.Platform, @"\Alinux/(?:amd64|arm64|arm/v7)\z"))
            throw new ArgumentException("Invalid configured Compose channel/platform.");
        if (!Regex.IsMatch(compose.ProjectName, @"\A[a-z0-9][a-z0-9_-]{0,62}\z") || !Path.IsPathFullyQualified(compose.ProjectDirectory) || !Directory.Exists(compose.ProjectDirectory))
            throw new ArgumentException("Invalid Compose project.");
        if (compose.Files.Count == 0 || compose.Files.Any(f => !Path.IsPathFullyQualified(f) || !File.Exists(f)))
            throw new ArgumentException("Configure existing absolute Compose files.");
        if (compose.Services.Count == 0 || compose.Services.Values.Distinct(StringComparer.Ordinal).Count() != compose.Services.Count || compose.Services.Any(p => !Regex.IsMatch(p.Key, @"\A[a-z][a-z0-9_-]{0,62}\z") || !Name(p.Value)))
            throw new ArgumentException("Configure unique service names for allowed image roles.");
        if (compose.CommandTimeoutSeconds < 1 || options.CheckTimeoutSeconds < 1 || compose.AllowedImageRepositories.Count == 0 || compose.KeepTransactions < 1)
            throw new ArgumentException("Invalid Compose limits or repository allow-list.");
        foreach (string value in new[] { options.CatalogUrl, options.PublicUrl, options.ApiVersionUrl ?? "" })
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 || uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
                throw new ArgumentException("Use trusted HTTPS or loopback HTTP endpoints.");
        if (!compose.WasmPath.StartsWith('/') || compose.WasmPath.Contains(':') || compose.WasmPath.Split('/').Any(p => p == ".."))
            throw new ArgumentException("Invalid image WASM directory.");
    }
    static bool Name(string value) => Regex.IsMatch(value, @"\A[a-zA-Z0-9][a-zA-Z0-9_.-]{0,62}\z");
    /// <summary>Validates optional catalog metadata without changing old ZIP releases.</summary>
    public static void ValidateMetadata(GXComposeRelease descriptor)
    {
        if (descriptor.Channel is not ("stable" or "preview") || descriptor.Platforms.Count == 0 || descriptor.Platforms.Any(p => !Regex.IsMatch(p, @"\Alinux/(?:amd64|arm64|arm/v7)\z")) || descriptor.Images.Count == 0)
            throw new InvalidDataException("Invalid Compose release channel, platform or image roles.");
        foreach (var entry in descriptor.Images)
        {
            var parts = entry.Value.Split("@sha256:", StringSplitOptions.None);
            if (!Regex.IsMatch(entry.Key, @"\A[a-z][a-z0-9_-]{0,62}\z") || parts.Length != 2 || !Regex.IsMatch(parts[0], @"\A[a-z0-9][a-z0-9._:/-]*[a-z0-9]\z") || parts[0].Contains("://") || !Regex.IsMatch(parts[1], @"\A[a-f0-9]{64}\z"))
                throw new InvalidDataException("Compose images must use immutable sha256 digest references.");
        }
        if (descriptor.RollbackCompatibleFrom is not null && (!VersionRange.TryParse(descriptor.RollbackCompatibleFrom, out var range) || !range.HasLowerBound || !range.HasUpperBound))
            throw new InvalidDataException("An audited bounded database rollback compatibility range is required.");
    }
    /// <summary>Enforces digest, channel, platform, role and audited rollback policy.</summary>
    public static void ValidateRelease(GXDeploymentOptions options, GXUpdateRelease release, string currentVersion)
    {
        GXDeploymentQueue.ValidateVersion(release.Version);
        var settings = options.Compose ?? throw new ArgumentException("Compose settings are missing.");
        var descriptor = release.Compose ?? throw new InvalidDataException("Release has no Compose images.");
        ValidateMetadata(descriptor);
        if (release.IsRevoked || release.IsUnsupported || release.IsPrerelease && settings.Channel != "preview" || descriptor.Channel != settings.Channel || !descriptor.Platforms.Contains(settings.Platform, StringComparer.Ordinal))
            throw new InvalidDataException("Release is unavailable for this channel/platform.");
        if (!descriptor.Images.Keys.Order().SequenceEqual(settings.Services.Keys.Order()) || descriptor.Images.Values.Any(image => !settings.AllowedImageRepositories.Contains(image.Split('@')[0], StringComparer.Ordinal)))
            throw new InvalidDataException("Release image roles/repositories do not match the configured installation.");
        if (descriptor.ContainsWasm && !settings.Services.ContainsKey(settings.WasmRole))
            throw new InvalidDataException("Configure the WASM image role.");
        if (!NuGetVersion.TryParse(currentVersion, out var current) || NuGetVersion.Parse(release.Version) < current)
            throw new InvalidDataException("Cannot automatically downgrade this installation.");
        if (descriptor.MaintenanceRequired || descriptor.RollbackCompatibleFrom is null || !VersionRange.Parse(descriptor.RollbackCompatibleFrom).Satisfies(current))
            throw new GXMaintenanceUpdateRequiredException("This release requires a maintenance update with database backup and restore; rollback compatibility has not been audited for the installed version.");
    }
    /// <summary>Resolves exactly the requested version from the configured trusted catalog product.</summary>
    public static GXUpdateRelease SelectRelease(GXUpdateCatalog catalog, GXDeploymentOptions options, string version)
    {
        GXDeploymentQueue.ValidateVersion(version);
        return catalog.Items.SingleOrDefault(i => i.Id == options.ProductId)?.Releases.SingleOrDefault(r => r.Version == version)
            ?? throw new InvalidDataException("Requested release is absent from the trusted catalog.");
    }
    static string Override(GXDeploymentOptions options) => Path.Combine(options.Root, "compose.override.json");
    static List<string> Arguments(GXDeploymentOptions options, string? snapshot = null)
    {
        var settings = options.Compose!;
        List<string> result = ["compose", "--project-name", settings.ProjectName, "--project-directory", settings.ProjectDirectory];
        foreach (var file in snapshot is null ? settings.Files : [snapshot])
        { result.Add("-f"); result.Add(file); }
        if (File.Exists(Override(options)))
        { result.Add("-f"); result.Add(Override(options)); }
        return result;
    }
    async Task<string[]> ContainersAsync(GXDeploymentOptions options, string service, string? snapshot, CancellationToken token)
    {
        var args = Arguments(options, snapshot);
        args.AddRange(["ps", "--all", "--quiet", service]);
        var ids = (await runner.RunAsync(args, token)).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (ids.Length == 0 || ids.Any(id => !Regex.IsMatch(id, @"\A[a-f0-9]{12,64}\z")))
            throw new IOException("Configured Compose service has no container: " + service);
        return ids;
    }
    async Task<JsonDocument> InspectAsync(string id, CancellationToken token) => JsonDocument.Parse(await runner.RunAsync(["inspect", id], token));
    static void ValidateContainer(JsonElement container, GXDeploymentOptions options, string service, bool healthy)
    {
        var labels = container.GetProperty("Config").GetProperty("Labels");
        if (labels.GetProperty("com.docker.compose.project").GetString() != options.Compose!.ProjectName || labels.GetProperty("com.docker.compose.service").GetString() != service)
            throw new InvalidDataException("Container is outside the configured Compose project/service.");
        var state = container.GetProperty("State");
        if (!state.GetProperty("Running").GetBoolean() || healthy && (!state.TryGetProperty("Health", out var health) || health.GetProperty("Status").GetString() != "healthy"))
            throw new IOException("Compose service is not healthy: " + service);
    }
    /// <summary>Pulls, snapshots, replaces, verifies and if necessary restores the fixed service group.</summary>
    public async Task InstallAsync(GXDeploymentOptions options, GXUpdateRelease release, GXDeploymentRequest request, CancellationToken token = default)
    {
        ValidateOptions(options);
        if (request.Version != release.Version || !Guid.TryParseExact(request.RequestId, "N", out _))
            throw new ArgumentException("Invalid update transaction identity.");
        Directory.CreateDirectory(options.Root);
        using var gate = new FileStream(Path.Combine(options.Root, "deployment.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var queue = new GXDeploymentQueue(options.Root);
        var status = new GXDeploymentStatus { Version = release.Version, RequestId = request.RequestId, State = "Downloading" };
        bool activation = false;
        try
        {
            string currentVersion = await ReadVersionAsync(options, token);
            ValidateRelease(options, release, currentVersion);
            await queue.WriteStatusAsync(status, token);
            string directory = Path.Combine(options.Root, "transactions", request.RequestId!);
            Directory.CreateDirectory(directory);
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var configArgs = Arguments(options);
            configArgs.AddRange(["config", "--format", "json"]);
            string snapshot = await runner.RunAsync(configArgs, token);
            using (var config = JsonDocument.Parse(snapshot))
                foreach (string service in options.Compose!.Services.Values)
                    if (!config.RootElement.GetProperty("services").TryGetProperty(service, out _))
                        throw new InvalidDataException("Configured service is absent from Compose configuration.");
            var transaction = new GXComposeTransaction
            {
                PreviousVersion = currentVersion,
                SnapshotFile = Path.Combine(directory, "compose.previous.json"),
                ContainsWasm = release.Compose!.ContainsWasm,
                NewImages = release.Compose.Images.ToDictionary(p => options.Compose!.Services[p.Key], p => p.Value, StringComparer.Ordinal)
            };
            if (transaction.ContainsWasm)
            {
                using var marker = JsonDocument.Parse(await client.GetStringAsync(new Uri(new Uri(options.PublicUrl.TrimEnd('/') + "/"), "version.json?gxupdate=" + Guid.NewGuid().ToString("N")), token));
                transaction.PreviousWasmVersion = marker.RootElement.GetProperty("version").GetString();
                GXDeploymentQueue.ValidateVersion(transaction.PreviousWasmVersion ?? "");
            }
            await WritePrivateAsync(transaction.SnapshotFile, snapshot, token);
            transaction.SnapshotSha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(snapshot)));
            foreach (var role in options.Compose!.Services)
            {
                var ids = await ContainersAsync(options, role.Value, null, token);
                if (ids.Length != 1)
                    throw new InvalidDataException("Automatic update requires one container per configured service.");
                using var inspect = await InspectAsync(ids[0], token);
                var container = inspect.RootElement[0];
                ValidateContainer(container, options, role.Value, healthy: true);
                string image = container.GetProperty("Image").GetString()!;
                if (!Regex.IsMatch(image, @"\Asha256:[a-f0-9]{64}\z"))
                    throw new InvalidDataException("Current container has no immutable image ID.");
                string backup = "gurux-updater-backup:" + request.RequestId + "-" + role.Key.ToLowerInvariant();
                await runner.RunAsync(["image", "tag", image, backup], token);
                transaction.PreviousImages.Add(role.Value, backup);
                transaction.PreviousImageIds.Add(role.Value, image);
            }
            status.Compose = transaction;
            await queue.WriteStatusAsync(status, token);
            foreach (var image in release.Compose.Images.Values.Distinct(StringComparer.Ordinal))
            {
                await runner.RunAsync(["pull", "--platform", options.Compose.Platform, image], token);
                using var inspect = JsonDocument.Parse(await runner.RunAsync(["image", "inspect", image], token));
                var item = inspect.RootElement[0];
                string imageId = item.GetProperty("Id").GetString()!;
                if (!Regex.IsMatch(imageId, @"\Asha256:[a-f0-9]{64}\z"))
                    throw new InvalidDataException("Pulled image has no immutable local ID.");
                foreach (var role in release.Compose.Images.Where(p => p.Value == image))
                    transaction.NewImageIds[options.Compose.Services[role.Key]] = imageId;
                string platform = item.GetProperty("Os").GetString() + "/" + item.GetProperty("Architecture").GetString();
                var selectedPlatform = options.Compose.Platform.Split('/');
                bool wrongVariant = selectedPlatform.Length == 3 && (!item.TryGetProperty("Variant", out var variant) || variant.GetString() != selectedPlatform[2]);
                if (options.Compose.Platform != platform && !options.Compose.Platform.StartsWith(platform + "/", StringComparison.Ordinal) ||
                    wrongVariant ||
                    item.GetProperty("Config").GetProperty("Labels").GetProperty("org.opencontainers.image.version").GetString() != release.Version)
                    throw new InvalidDataException("Pulled image platform/version differs from the catalog.");
            }
            // Journal the old images/config BEFORE changing the durable override or replacing any container.
            status.State = "Activating";
            await queue.WriteStatusAsync(status, token);
            activation = true;
            await WriteOverrideAsync(options, transaction.NewImages, token);
            await UpAsync(options, transaction.SnapshotFile, token);
            status.State = "Verifying";
            await queue.WriteStatusAsync(status, token);
            await VerifyAsync(options, transaction, release.Version, token);
            status.State = "Succeeded";
            await queue.WriteStatusAsync(status, token);
            await CleanupAsync(options, status);
        }
        catch (Exception exception)
        {
            status.Error = exception.Message;
            status.State = activation ? "RollingBack" : exception is GXMaintenanceUpdateRequiredException ? "MaintenanceRequired" : "Failed";
            Exception? persistence = null;
            try
            { await queue.WriteStatusAsync(status); }
            catch (Exception ex) { persistence = ex; }
            if (activation)
            {
                try
                { await RestoreAsync(options, status.Compose!); status.State = "RolledBack"; }
                catch (Exception ex) { status.State = "RollbackFailed"; status.Error += " Restore: " + ex.Message; }
                try
                { await queue.WriteStatusAsync(status); }
                catch (Exception ex) { persistence = ex; }
            }
            if (persistence is not null)
                throw new AggregateException("Compose update outcome could not be persisted; recovery is required.", exception, persistence);
            if (status.State is "Failed" or "RolledBack" or "MaintenanceRequired")
                await CleanupAsync(options, status);
            throw;
        }
    }
    /// <summary>Explicitly restores only the latest successful deployment's verified retained predecessor.</summary>
    public async Task RestorePreviousAsync(GXDeploymentOptions options, GXDeploymentRequest request, CancellationToken token = default)
    {
        ValidateOptions(options);
        if (!request.RestorePrevious || !Guid.TryParseExact(request.RequestId, "N", out _)) throw new ArgumentException("An explicit restore transaction is required.");
        using var gate = new FileStream(Path.Combine(options.Root, "deployment.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string current = await ReadVersionAsync(options, token);
        var successful = await GXDeploymentRestoration.ReadPreviousAsync(options.Root, current, true, token);
        if (successful?.Compose is not { } previous || request.Version != previous.PreviousVersion)
            throw new InvalidDataException("Only the retained previous successful installation can be restored.");
        await ValidateSnapshotAsync(options, previous);
        await VerifyAsync(options, previous, current, token);
        // Journal the current candidate as the recovery target before changing any container.
        var recovery = new GXComposeTransaction
        {
            SnapshotFile = previous.SnapshotFile, SnapshotSha256 = previous.SnapshotSha256,
            PreviousVersion = current, PreviousWasmVersion = current, ContainsWasm = previous.ContainsWasm,
            PreviousImages = new(previous.NewImages), PreviousImageIds = new(previous.NewImageIds)
        };
        await ValidateSnapshotAsync(options, recovery);
        var status = new GXDeploymentStatus { RequestId = request.RequestId, Version = request.Version, RestorePrevious = true, Compose = recovery, State = "Activating" };
        var queue = new GXDeploymentQueue(options.Root);
        await queue.WriteStatusAsync(status, token);
        try
        {
            await RestoreAsync(options, previous);
            status.State = "Succeeded";
            await queue.WriteStatusAsync(status);
        }
        catch (Exception error)
        {
            status.Error = error.Message; status.State = "RollingBack";
            Exception? persistence = null;
            try { await queue.WriteStatusAsync(status); } catch (Exception ex) { persistence = ex; }
            try { await RestoreAsync(options, recovery); status.State = "RolledBack"; }
            catch (Exception ex) { status.State = "RollbackFailed"; status.Error += " Restore: " + ex.Message; }
            try { await queue.WriteStatusAsync(status); } catch (Exception ex) { persistence = ex; }
            if (persistence is not null) throw new AggregateException("Restore outcome could not be persisted.", error, persistence);
            throw;
        }
    }
    async Task UpAsync(GXDeploymentOptions options, string snapshot, CancellationToken token)
    {
        var args = Arguments(options, snapshot);
        args.AddRange(["up", "-d", "--no-deps", "--force-recreate", "--pull", "never"]);
        args.AddRange(options.Compose!.Services.Values);
        await runner.RunAsync(args, token);
    }
    static Task WriteOverrideAsync(GXDeploymentOptions options, Dictionary<string, string> images, CancellationToken token) => WritePrivateAsync(Override(options), JsonSerializer.Serialize(new { services = images.ToDictionary(p => p.Key, p => new { image = p.Value }) }, Json), token);
    static async Task WritePrivateAsync(string destination, string content, CancellationToken token)
    {
        string temporary = destination + "." + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (OperatingSystem.IsLinux())
                    File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                await using var writer = new StreamWriter(stream, leaveOpen: true);
                await writer.WriteAsync(content.AsMemory(), token);
                await writer.FlushAsync(token);
                stream.Flush(true);
            }
            File.Move(temporary, destination, true);
        }
        finally { File.Delete(temporary); }
    }
    async Task<string> ReadVersionAsync(GXDeploymentOptions options, CancellationToken token)
    {
        var uri = new UriBuilder(options.ApiVersionUrl!);
        uri.Query = uri.Query.TrimStart('?') + (uri.Query.Length == 0 ? "" : "&") + "gxupdate=" + Guid.NewGuid().ToString("N");
        using var message = new HttpRequestMessage(HttpMethod.Get, uri.Uri);
        message.Headers.CacheControl = new() { NoCache = true, NoStore = true };
        using var response = await client.SendAsync(message, token);
        response.EnsureSuccessStatusCode();
        string version = (await response.Content.ReadAsStringAsync(token)).Trim().Trim('"');
        GXDeploymentQueue.ValidateVersion(version);
        return version;
    }
    async Task VerifyAsync(GXDeploymentOptions options, GXComposeTransaction transaction, string version, CancellationToken token, bool restoring = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.CheckTimeoutSeconds));
        Exception? last = null;
        try
        {
            while (true)
            {
                try
                {
                    foreach (string service in options.Compose!.Services.Values)
                        foreach (string id in await ContainersAsync(options, service, transaction.SnapshotFile, timeout.Token))
                        {
                            using var inspect = await InspectAsync(id, timeout.Token);
                            ValidateContainer(inspect.RootElement[0], options, service, healthy: true);
                            var ids = restoring ? transaction.PreviousImageIds : transaction.NewImageIds;
                            if (!ids.TryGetValue(service, out var expectedImage) || inspect.RootElement[0].GetProperty("Image").GetString() != expectedImage)
                                throw new IOException("Compose service is running an unexpected image ID.");
                        }
                    if (NuGetVersion.Parse(await ReadVersionAsync(options, timeout.Token)).Version != NuGetVersion.Parse(version).Version)
                        throw new IOException("Public AMI endpoint reports an unexpected version.");
                    break;
                }
                catch (Exception ex) when (ex is IOException or HttpRequestException) { last = ex; }
                await Task.Delay(250, timeout.Token);
            }
            if (transaction.ContainsWasm)
            {
                string service = options.Compose!.Services[options.Compose.WasmRole];
                string id = (await ContainersAsync(options, service, transaction.SnapshotFile, timeout.Token)).Single();
                string web = Path.Combine(Path.GetDirectoryName(transaction.SnapshotFile)!, "wwwroot-" + Guid.NewGuid().ToString("N"));
                try
                {
                    await runner.RunAsync(["cp", id + ":" + options.Compose.WasmPath, web], timeout.Token);
                    await new GXDeploymentService(client).VerifyPublicWasmAsync(web, options.PublicUrl, restoring ? transaction.PreviousWasmVersion ?? version : version, options.CheckTimeoutSeconds, timeout.Token);
                }
                finally { if (Directory.Exists(web)) Directory.Delete(web, true); }
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Compose health/version verification timed out. " + last?.Message, last); }
    }
    static async Task ValidateSnapshotAsync(GXDeploymentOptions options, GXComposeTransaction transaction)
    {
        string snapshot = Path.GetFullPath(transaction.SnapshotFile);
        string transactions = Path.Combine(options.Root, "transactions") + Path.DirectorySeparatorChar;
        if (!snapshot.StartsWith(transactions, StringComparison.Ordinal) || !File.Exists(snapshot) || new FileInfo(snapshot).LinkTarget is not null ||
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(snapshot))) != transaction.SnapshotSha256 ||
            !transaction.PreviousImages.Keys.Order().SequenceEqual(options.Compose!.Services.Values.Order()) ||
            !transaction.PreviousImageIds.Keys.Order().SequenceEqual(options.Compose.Services.Values.Order()))
            throw new InvalidDataException("Compose recovery snapshot is unavailable or has changed.");
    }
    async Task RestoreAsync(GXDeploymentOptions options, GXComposeTransaction transaction)
    {
        await ValidateSnapshotAsync(options, transaction);
        await WriteOverrideAsync(options, transaction.PreviousImages, CancellationToken.None);
        await UpAsync(options, transaction.SnapshotFile, CancellationToken.None);
        await VerifyAsync(options, transaction, transaction.PreviousVersion, CancellationToken.None, restoring: true);
    }
    /// <summary>Restores an interrupted activation before the agent accepts additional work.</summary>
    public async Task RecoverAsync(GXDeploymentOptions options)
    {
        ValidateOptions(options);
        Directory.CreateDirectory(options.Root);
        using var gate = new FileStream(Path.Combine(options.Root, "deployment.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var queue = new GXDeploymentQueue(options.Root);
        var status = await queue.ReadStatusAsync();
        if (status?.State is "Activating" or "Verifying" or "RollingBack" or "RollbackFailed")
        {
            if (status.Compose is null)
                throw new InvalidDataException("An unresolved transaction belongs to a different installation method.");
            try
            { await RestoreAsync(options, status.Compose); status.State = "RolledBack"; status.Error = "Interrupted Compose update restored the previous healthy version."; }
            catch (Exception ex) { status.State = "RollbackFailed"; status.Error = ex.Message; await queue.WriteStatusAsync(status); throw; }
            await queue.WriteStatusAsync(status);
        }
        if (status?.State is "Succeeded" or "Failed" or "RolledBack" or "MaintenanceRequired")
        {
            await queue.WriteStatusAsync(status); // A crash may have committed the journal but not history.
            await CleanupAsync(options, status);
            if (status.RequestId is not null && (await queue.ReadRequestAsync())?.RequestId == status.RequestId)
                queue.CompleteRequest();
        }
    }
    async Task CleanupAsync(GXDeploymentOptions options, GXDeploymentStatus current)
    {
        // Never turn an optional retention failure into a failed installation or remove unresolved recovery data.
        try
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            string parent = Path.Combine(options.Root, "transactions");
            if (!Directory.Exists(parent))
                return;
            List<(DirectoryInfo Directory, GXDeploymentStatus Status)> candidates = [];
            foreach (var directory in new DirectoryInfo(parent).EnumerateDirectories())
            {
                if (directory.LinkTarget is not null || directory.Parent!.FullName != parent || !Guid.TryParseExact(directory.Name, "N", out _))
                    continue;
                string history = Path.Combine(options.Root, "history", directory.Name + ".json");
                if (!File.Exists(history))
                    continue;
                var status = JsonSerializer.Deserialize<GXDeploymentStatus>(await File.ReadAllTextAsync(history), Json);
                if (status?.State is "Succeeded" or "Failed" or "RolledBack" or "MaintenanceRequired")
                    candidates.Add((directory, status));
            }
            var protectedIds = new HashSet<string>(StringComparer.Ordinal);
            if (current.RequestId is not null)
                protectedIds.Add(current.RequestId);
            var successful = candidates.Where(c => c.Status.State == "Succeeded").OrderByDescending(c => c.Status.UpdatedAt).FirstOrDefault();
            if (successful.Directory is not null)
                protectedIds.Add(successful.Directory.Name);
            foreach (var candidate in candidates.Where(c => !protectedIds.Contains(c.Directory.Name)).OrderByDescending(c => c.Directory.LastWriteTimeUtc).Take(options.Compose!.KeepTransactions))
                protectedIds.Add(candidate.Directory.Name);
            var activeReferences = new HashSet<string>(StringComparer.Ordinal);
            if (File.Exists(Override(options)))
            {
                using var active = JsonDocument.Parse(await File.ReadAllTextAsync(Override(options)));
                foreach (var service in active.RootElement.GetProperty("services").EnumerateObject())
                    activeReferences.Add(service.Value.GetProperty("image").GetString()!);
            }
            if (current.Compose is not null)
                activeReferences.UnionWith(current.Compose.PreviousImages.Values);
            foreach (var candidate in candidates.Where(c => !protectedIds.Contains(c.Directory.Name)))
            {
                string prefix = "gurux-updater-backup:" + candidate.Directory.Name + "-";
                var images = (candidate.Status.Compose?.PreviousImages.Values ?? Enumerable.Empty<string>())
                    .Concat(options.Compose!.Services.Keys.Select(role => prefix + role)).Distinct(StringComparer.Ordinal);
                foreach (string image in images)
                    if (image.StartsWith(prefix, StringComparison.Ordinal) && !activeReferences.Contains(image))
                    {
                        try
                        { await runner.RunAsync(["image", "rm", image], cleanupTimeout.Token); }
                        catch (IOException) { } // Already absent or still used: leave Docker's reference protection intact.
                    }
                candidate.Directory.Delete(recursive: true);
            }
        }
        catch (Exception)
        { Console.Error.WriteLine("Compose transaction retention could not finish; retained data can be cleaned by the host administrator."); }
    }
}
