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
