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

using Gurux.Updater.Enums;
using Gurux.Updater.Model;
using System.Security.Cryptography;
using System.Text.Json;

namespace Gurux.Updater.Services;

/// <summary>
/// Downloads and manages device profile index documents and individual profile files.
/// </summary>
public sealed class GXDeviceProfileUpdateService
{
    private static readonly int SupportedSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonReadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The default URI of the Gurux DLMS device profile index.
    /// </summary>
    public static readonly Uri DefaultIndexUri =
        new("https://gurux.github.io/Gurux.DLMS.DeviceProfiles/manufacturers.json");

    private readonly HttpClient _client;

    /// <summary>
    /// Initializes the service with the supplied HTTP client.
    /// </summary>
    public GXDeviceProfileUpdateService(HttpClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Downloads and validates the default Gurux DLMS device profile index.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The validated device profile index.</returns>
    public Task<GXDeviceProfileIndex> GetIndexAsync(CancellationToken cancellationToken = default)
    {
        return GetIndexAsync(DefaultIndexUri, cancellationToken);
    }

    /// <summary>
    /// Downloads and validates a device profile index from the specified URI.
    /// </summary>
    /// <param name="indexUri">The absolute HTTPS URI of the index document.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The validated device profile index.</returns>
    public async Task<GXDeviceProfileIndex> GetIndexAsync(
        Uri indexUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(indexUri);
        if (!indexUri.IsAbsoluteUri)
        {
            throw new ArgumentException("Index URI must be absolute.", nameof(indexUri));
        }

        using HttpResponseMessage response = await _client.GetAsync(indexUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        GXDeviceProfileIndex index = await JsonSerializer.DeserializeAsync<GXDeviceProfileIndex>(
            stream,
            JsonReadOptions,
            cancellationToken)
            ?? throw new InvalidDataException("Index response was empty.");

        ValidateIndex(index);
        return index;
    }

    /// <summary>
    /// Returns a flat list of all profile entries across all manufacturers, models, and versions.
    /// Model-level entries (no device version) and version-specific entries are both included
    /// but are never merged.
    /// </summary>
    /// <param name="index">The device profile index.</param>
    /// <returns>All profile entries in the index.</returns>
    public IReadOnlyList<GXDeviceProfileEntry> GetProfiles(GXDeviceProfileIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        return EnumerateEntries(index).ToArray();
    }

    /// <summary>
    /// Compares the supplied installed profiles against the index and reports the status of each.
    /// </summary>
    /// <param name="index">The device profile index.</param>
    /// <param name="installedProfiles">The profiles currently installed by the host application.</param>
    /// <returns>A status entry for each installed profile.</returns>
    public IReadOnlyList<GXDeviceProfileStatus> GetStatus(
        GXDeviceProfileIndex index,
        IEnumerable<GXInstalledProfile> installedProfiles)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(installedProfiles);

        Dictionary<string, GXDeviceProfileEntry> byId = EnumerateEntries(index)
            .ToDictionary(e => e.Id, StringComparer.Ordinal);

        List<GXDeviceProfileStatus> results = [];
        foreach (GXInstalledProfile installed in installedProfiles)
        {
            if (!byId.TryGetValue(installed.Id, out GXDeviceProfileEntry? entry))
            {
                results.Add(new GXDeviceProfileStatus
                {
                    Id = installed.Id,
                    Kind = GXDeviceProfileStatusKind.NotInIndex,
                    InstalledSha256 = installed.Sha256
                });
                continue;
            }

            GXDeviceProfileStatusKind kind = string.Equals(
                installed.Sha256.Replace("-", string.Empty),
                entry.Sha256.Replace("-", string.Empty),
                StringComparison.OrdinalIgnoreCase)
                ? GXDeviceProfileStatusKind.Unchanged
                : GXDeviceProfileStatusKind.Changed;

            results.Add(new GXDeviceProfileStatus
            {
                Id = installed.Id,
                Kind = kind,
                InstalledSha256 = installed.Sha256,
                Entry = entry
            });
        }
        return results;
    }

    /// <summary>
    /// Downloads a device profile, verifies its integrity, and writes it to the destination path.
    /// The destination file is not modified unless all checks pass.
    /// </summary>
    /// <param name="entry">The index entry describing the profile to download.</param>
    /// <param name="destination">The file path where the verified profile is written.</param>
    /// <param name="validator">
    /// An optional callback that receives the path to the verified temp file and may perform
    /// additional structural validation. Throw to abort the installation.
    /// </param>
    /// <param name="progress">An optional progress sink for byte counts.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public async Task DownloadProfileAsync(
        GXDeviceProfileEntry entry,
        string destination,
        Func<string, CancellationToken, Task>? validator = null,
        IProgress<GXUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new ArgumentException("Destination path is required.", nameof(destination));
        }
        if (!Uri.TryCreate(entry.Location, UriKind.Absolute, out Uri? locationUri) ||
            !string.Equals(locationUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Profile '{entry.Id}' location must be an absolute HTTPS URI.");
        }

        string tempPath = destination + ".tmp" + Guid.NewGuid().ToString("N");
        try
        {
            await DownloadToTempAsync(entry.Location, tempPath, progress, cancellationToken);
            VerifyDownloadedFile(entry, tempPath);

            if (validator is not null)
            {
                await validator(tempPath, cancellationToken);
            }

            string? directory = Path.GetDirectoryName(Path.GetFullPath(destination));
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            File.Move(tempPath, destination, overwrite: true);
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    private async Task DownloadToTempAsync(
        string url,
        string tempPath,
        IProgress<GXUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _client.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? 0;
        long bytesReceived = 0;
        byte[] buffer = new byte[81920];

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream dest = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            bytesReceived += read;
            progress?.Report(new GXUpdateProgress
            {
                BytesReceived = bytesReceived,
                TotalBytes = totalBytes
            });
        }
    }

    private static void VerifyDownloadedFile(GXDeviceProfileEntry entry, string tempPath)
    {
        byte[] bytes = File.ReadAllBytes(tempPath);

        string actualHash = Convert.ToHexString(SHA256.HashData(bytes));
        string expectedHash = entry.Sha256.Replace("-", string.Empty);
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"SHA-256 mismatch for profile '{entry.Id}'. Expected {expectedHash}, got {actualHash}.");
        }

        if (entry.Size.HasValue && bytes.LongLength != entry.Size.Value)
        {
            throw new InvalidDataException(
                $"Size mismatch for profile '{entry.Id}'. Expected {entry.Size.Value}, got {bytes.LongLength}.");
        }

        try
        {
            JsonDocument.Parse(bytes);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Profile '{entry.Id}' does not contain valid JSON.", ex);
        }
    }

    private static void ValidateIndex(GXDeviceProfileIndex index)
    {
        if (index.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported device profile index schema version '{index.SchemaVersion}'.");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (GXDeviceProfileEntry entry in EnumerateEntries(index))
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                throw new InvalidDataException("Device profile entry ID is required.");
            }
            if (!ids.Add(entry.Id))
            {
                throw new InvalidDataException($"Duplicate device profile ID '{entry.Id}'.");
            }
            if (string.IsNullOrWhiteSpace(entry.Sha256) || entry.Sha256.Replace("-", string.Empty).Length != 64)
            {
                throw new InvalidDataException(
                    $"Profile '{entry.Id}' sha256 must be 64 hexadecimal characters.");
            }
            if (!Uri.TryCreate(entry.Location, UriKind.Absolute, out Uri? uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Profile '{entry.Id}' location must be an absolute HTTPS URI.");
            }
            if (entry.Size.HasValue && entry.Size.Value < 0)
            {
                throw new InvalidDataException(
                    $"Profile '{entry.Id}' size must be non-negative.");
            }
        }
    }

    /// <summary>
    /// Enumerates all profile entries from both model-level and version-level settings.
    /// Model-level entries and version-specific entries are independent: no merging occurs.
    /// </summary>
    private static IEnumerable<GXDeviceProfileEntry> EnumerateEntries(GXDeviceProfileIndex index)
    {
        foreach (GXDeviceProfileManufacturer manufacturer in index.Manufacturers)
        {
            foreach (GXDeviceProfileModel model in manufacturer.Models)
            {
                // Structure: <manufacturer>/<model>/<profile>
                foreach (GXDeviceProfileEntry entry in model.Settings)
                {
                    yield return entry;
                }

                // Structure: <manufacturer>/<model>/<version>/<profile>
                foreach (GXDeviceProfileVersion version in model.Versions)
                {
                    foreach (GXDeviceProfileEntry entry in version.Settings)
                    {
                        yield return entry;
                    }
                }
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best effort
        }
    }
}