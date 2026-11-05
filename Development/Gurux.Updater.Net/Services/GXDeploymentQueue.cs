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

using System.Text.Json;
using System.Text.RegularExpressions;
using Gurux.Updater.Model;

namespace Gurux.Updater.Services;

/// <summary>Installation-local queue. Keep its directory outside release directories and restrict write permissions.</summary>
public sealed class GXDeploymentQueue(string root)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    /// <summary>Absolute installation root.</summary>
    public string Root { get; } = Path.GetFullPath(root);
    /// <summary>Rejects versions that could contain paths or shell syntax.</summary>
    public static void ValidateVersion(string version)
    {
        if (version.Length > 80 || !Regex.IsMatch(version, @"\A\d+\.\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?\z"))
            throw new ArgumentException("A numeric release version is required.", nameof(version));
    }
    /// <summary>Publishes one version-only request with a fresh internal transaction ID.</summary>
    public async Task EnqueueAsync(string version, CancellationToken token = default)
    {
        ValidateVersion(version);
        Directory.CreateDirectory(Path.Combine(Root, "queue"));
        // A separate publication lock keeps readers from seeing partially written JSON.
        using var gate = new FileStream(Path.Combine(Root, "queue", "queue.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(Path.Combine(Root, "queue", "request.json")))
            throw new IOException("An update is already queued or running.");
        await WriteAtomicAsync("queue/request.json", new GXDeploymentRequest(version, Guid.NewGuid().ToString("N")), token);
    }
    /// <summary>Reads the atomically published pending request.</summary>
    public async Task<GXDeploymentRequest?> ReadRequestAsync(CancellationToken token = default) => await ReadAsync<GXDeploymentRequest>("queue/request.json", token);
    /// <summary>Reads the last persisted transaction state.</summary>
    public async Task<GXDeploymentStatus?> ReadStatusAsync(CancellationToken token = default) => await ReadAsync<GXDeploymentStatus>("status.json", token);
    /// <summary>Atomically persists the transaction journal.</summary>
    public Task WriteStatusAsync(GXDeploymentStatus status, CancellationToken token = default)
    { status.UpdatedAt = DateTimeOffset.UtcNow; return PersistStatusAsync(status, token); }
    async Task PersistStatusAsync(GXDeploymentStatus status, CancellationToken token)
    {
        if (status.RequestId is not null && !Regex.IsMatch(status.RequestId, @"\A[A-Za-z0-9_-]{1,80}\z")) throw new InvalidDataException("Invalid transaction ID.");
        await WriteAtomicAsync("status.json", status, token);
        if (status.RequestId is not null)
        {
            Directory.CreateDirectory(Path.Combine(Root, "history"));
            await WriteAtomicAsync("history/" + status.RequestId + ".json", status, token);
        }
    }
    /// <summary>Acknowledges the processed queue entry.</summary>
    public void CompleteRequest()
    {
        var path = Path.Combine(Root, "queue", "request.json");
        if (File.Exists(path))
        {
            var request = JsonSerializer.Deserialize<GXDeploymentRequest>(File.ReadAllText(path), Json);
            if (Guid.TryParseExact(request?.RequestId, "N", out _)) File.Delete(Path.Combine(Root, "queue", request!.RequestId + ".ready"));
        }
        File.Delete(path);
    }
    async Task<T?> ReadAsync<T>(string name, CancellationToken token)
    {
        try
        {
            await using var file = new FileStream(Path.Combine(Root, name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return await JsonSerializer.DeserializeAsync<T>(file, Json, token);
        }
        catch (FileNotFoundException) { return default; }
        catch (DirectoryNotFoundException) { return default; }
    }
    async Task WriteAtomicAsync<T>(string name, T value, CancellationToken token)
    {
        Directory.CreateDirectory(Root);
        string temporary = Path.Combine(Path.GetDirectoryName(Path.Combine(Root, name))!, "." + Path.GetFileName(name) + "." + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(file, value, Json, token);
                file.Flush(true);
            }
            File.Move(temporary, Path.Combine(Root, name), true);
        }
        finally { File.Delete(temporary); }
    }
}
