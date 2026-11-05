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

using System.Text.Json.Serialization;

namespace Gurux.Updater.Model;


/// <summary>
/// Represents an asset returned by the GitHub releases API.
/// </summary>
internal sealed class GXGitHubAsset
{
    /// <summary>
    /// Gets the release asset file name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
    /// <summary>
    /// Gets the URL used to download the release asset.
    /// </summary>
    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; init; } = string.Empty;
    /// <summary>
    /// Gets the asset size in bytes.
    /// </summary>
    [JsonPropertyName("size")]
    public long Size
    {
        get; init;
    }
}
