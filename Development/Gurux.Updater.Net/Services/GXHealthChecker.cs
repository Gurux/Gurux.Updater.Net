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

using System.Text.Json;

namespace Gurux.Updater;

/// <summary>
/// Polls HTTP health and version endpoints until the application is ready.
/// </summary>
public sealed class GXHealthChecker(HttpClient client)
{
    /// <summary>
    /// Waits for the configured health and version endpoints to succeed, or returns immediately when neither is configured.
    /// </summary>
    public async Task WaitAsync(Uri? healthUrl, Uri? versionUrl, string expectedVersion, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (healthUrl is null && versionUrl is null)
        {
            return;
        }
        DateTimeOffset end = DateTimeOffset.UtcNow + timeout;
        Exception? lastError = null;
        while (DateTimeOffset.UtcNow < end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (healthUrl is not null)
                {
                    using HttpResponseMessage response = await client.GetAsync(healthUrl, cancellationToken);
                    response.EnsureSuccessStatusCode();
                }
                if (versionUrl is not null)
                {
                    string json = await client.GetStringAsync(versionUrl, cancellationToken);
                    using JsonDocument document = JsonDocument.Parse(json);
                    string? version = document.RootElement.TryGetProperty("version", out JsonElement value) ? value.GetString() : null;
                    if (!string.Equals(Normalize(version), Normalize(expectedVersion), StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Expected version {expectedVersion}, but the server reported {version ?? "<null>"}.");
                    }
                }
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
            {
                lastError = ex;
            }
            await Task.Delay(2000, cancellationToken);
        }
        throw new TimeoutException($"Application did not become healthy within {timeout.TotalSeconds:0} seconds. {lastError?.Message}");
    }

    /// <summary>
    /// Trims whitespace and leading v characters and removes build metadata from a version string.
    /// </summary>
    private static string? Normalize(string? value)
    {
        return value?.Trim().TrimStart('v', 'V').Split('+')[0];
    }
}
