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
/// Represents release metadata returned by the GitHub releases API.
/// </summary>
internal sealed class GXGitHubRelease
{
    /// <summary>
    /// Gets the Git tag associated with the release.
    /// </summary>
    [JsonPropertyName("tag_name")]
    public string TagName { get; init; } = string.Empty;
    /// <summary>
    /// Gets the URL of the GitHub release page.
    /// </summary>
    [JsonPropertyName("html_url")]
    public string? HtmlUrl
    {
        get; init;
    }
    /// <summary>
    /// Gets the release notes supplied by GitHub.
    /// </summary>
    [JsonPropertyName("body")]
    public string? Body
    {
        get; init;
    }
    /// <summary>
    /// Gets a value indicating whether the release is a draft.
    /// </summary>
    [JsonPropertyName("draft")]
    public bool Draft
    {
        get; init;
    }
    /// <summary>
    /// Gets a value indicating whether the release is marked as a prerelease.
    /// </summary>
    [JsonPropertyName("prerelease")]
    public bool Prerelease
    {
        get; init;
    }
    /// <summary>
    /// Gets the release publish time reported by GitHub.
    /// </summary>
    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt
    {
        get; init;
    }
    /// <summary>
    /// Gets the downloadable assets attached to the release.
    /// </summary>
    [JsonPropertyName("assets")]
    public List<GXGitHubAsset> Assets { get; init; } = [];
}