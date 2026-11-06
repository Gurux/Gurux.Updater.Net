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

using System.Security.Cryptography;
using System.Text.Json;
using Gurux.Updater.Model;

namespace Gurux.Updater.Services;

/// <summary>Resolves a restore only from the latest successful installation's retained local journal.</summary>
public static class GXDeploymentRestoration
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    /// <summary>Returns a validated previous installation, or null when no restore is available.</summary>
    public static Task<GXDeploymentStatus?> ReadPreviousAsync(string root, string? currentVersion, bool compose, CancellationToken token = default) => ReadPreviousCoreAsync(root, currentVersion, compose, true, token);
    /// <summary>Reads public journal eligibility only. The host agent must privately validate the saved snapshot before activation.</summary>
    public static Task<GXDeploymentStatus?> ReadEligiblePreviousAsync(string root, string? currentVersion, bool compose, CancellationToken token = default) => ReadPreviousCoreAsync(root, currentVersion, compose, false, token);
    static async Task<GXDeploymentStatus?> ReadPreviousCoreAsync(string root, string? currentVersion, bool compose, bool validateSnapshot, CancellationToken token)
    {
        string history = Path.Combine(root, "history");
        if (!Directory.Exists(history) || currentVersion is null)
        {
            return null;
        }
        GXDeploymentStatus? latest = null;
        foreach (string path in Directory.EnumerateFiles(history, "*.json"))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _) || new FileInfo(path).LinkTarget is not null)
            {
                continue;
            }
            GXDeploymentStatus? entry;
            try { entry = JsonSerializer.Deserialize<GXDeploymentStatus>(await File.ReadAllTextAsync(path, token), Json); }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
            if (entry?.State == "Succeeded" && (latest is null || entry.UpdatedAt > latest.UpdatedAt))
            {
                latest = entry;
            }
        }
        if (latest is null || latest.RestorePrevious || latest.Version != currentVersion || !Guid.TryParseExact(latest.RequestId, "N", out _))
        {
            return null;
        }
        try
        {
            if (compose)
            {
                var transaction = latest.Compose;
                if (transaction is null || transaction.PreviousVersion == currentVersion)
                {
                    return null;
                }
                GXDeploymentQueue.ValidateVersion(transaction.PreviousVersion);
                string directory = Path.Combine(root, "transactions", latest.RequestId!);
                if (Path.GetFullPath(transaction.SnapshotFile) != Path.Combine(directory, "compose.previous.json") ||
                    transaction.PreviousImages.Count == 0 || transaction.PreviousImageIds.Count != transaction.PreviousImages.Count)
                {
                    return null;
                }
                if (validateSnapshot && (new DirectoryInfo(directory).LinkTarget is not null ||
                    !File.Exists(transaction.SnapshotFile) || new FileInfo(transaction.SnapshotFile).LinkTarget is not null ||
                    Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(transaction.SnapshotFile, token))) != transaction.SnapshotSha256))
                {
                    return null;
                }
            }
            else
            {
                if (latest.Compose is not null || latest.PreviousTarget is null)
                {
                    return null;
                }
                string previous = Path.GetFullPath(Path.Combine(root, latest.PreviousTarget));
                if (Path.GetDirectoryName(previous) != Path.Combine(root, "releases"))
                {
                    return null;
                }
                string version = Path.GetFileName(previous);
                GXDeploymentQueue.ValidateVersion(version);
                if (version == currentVersion)
                {
                    return null;
                }
                if (validateSnapshot)
                {
                    if (!Directory.Exists(previous) || new DirectoryInfo(previous).LinkTarget is not null)
                    {
                        return null;
                    }
                    string marker = File.Exists(Path.Combine(previous, "wwwroot", "version.json")) ? Path.Combine(previous, "wwwroot", "version.json") : Path.Combine(previous, "version.json");
                    using var json = JsonDocument.Parse(await File.ReadAllTextAsync(marker, token));
                    if (json.RootElement.GetProperty("version").GetString() != version)
                    {
                        return null;
                    }
                }
            }
            return latest;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or JsonException or KeyNotFoundException or UnauthorizedAccessException) { return null; }
    }
    /// <summary>Gets the previous version from an already validated journal.</summary>
    public static string PreviousVersion(GXDeploymentStatus status) => status.Compose?.PreviousVersion ?? Path.GetFileName(status.PreviousTarget!);
}
