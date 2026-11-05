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

namespace Gurux.Updater.Services;

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
