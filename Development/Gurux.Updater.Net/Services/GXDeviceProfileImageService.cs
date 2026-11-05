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
using Gurux.Updater.Model;

namespace Gurux.Updater.Services;

/// <summary>Downloads optional device profile images into a verified file cache.</summary>
public sealed class GXDeviceProfileImageService(HttpClient client)
{
    /// <summary>Gets a cached image path, downloading and verifying the bytes when necessary.</summary>
    /// <param name="image">Image metadata from the manufacturer index.</param>
    /// <param name="cacheDirectory">Directory for hash-named PNG files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The absolute path of the verified image.</returns>
    public async Task<string> GetImageAsync(GXDeviceProfileImage image, string cacheDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        if (!Uri.TryCreate(image.Location, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            image.Sha256 is null || image.Sha256.Length != 64 || !image.Sha256.All(Uri.IsHexDigit) || image.Size < 0)
        {
            throw new InvalidDataException("Image requires an absolute HTTPS location, a SHA-256 checksum and a non-negative size.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        string destination = Path.Combine(Path.GetFullPath(cacheDirectory), image.Sha256.ToLowerInvariant() + ".png");
        if (File.Exists(destination) && await MatchesAsync(destination, image, cancellationToken))
        {
            return destination;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    if (read > image.Size - received)
                    {
                        throw new InvalidDataException("Image exceeds its declared size.");
                    }
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                }
            }
            if (!await MatchesAsync(temporary, image, cancellationToken))
            {
                throw new InvalidDataException("Image checksum or size mismatch.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static async Task<bool> MatchesAsync(string path, GXDeviceProfileImage image, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        if (stream.Length != image.Size)
            return false;
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).Equals(image.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
