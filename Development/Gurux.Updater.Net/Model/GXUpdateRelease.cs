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
using Gurux.Updater.Enums;

namespace Gurux.Updater.Model;

/// <summary>
/// Describes a release available for an update target.
/// </summary>
public sealed class GXUpdateRelease
{
    /// <summary>Optional Compose installation metadata; old ZIP catalogs omit this property.</summary>
    [JsonPropertyName("compose")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GXComposeRelease? Compose { get; init; }
    /// <summary>Gets the publisher-declared supported AMI version range; null means unknown.</summary>
    [JsonPropertyName("amiVersionRange")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AmiVersionRange { get; init; }
    /// <summary>Gets exact assembly dependencies; null means metadata is unavailable.</summary>
    [JsonPropertyName("references")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<GXAssemblyReference>? References { get; init; }
    /// <summary>Gets or sets publisher-declared urgency.</summary>
    [JsonPropertyName("priority")]
    public GXUpdatePriority Priority { get; set; } = GXUpdatePriority.Normal;
    /// <summary>Gets or sets whether this release contains declared security fixes.</summary>
    [JsonPropertyName("isSecurityUpdate")]
    public bool IsSecurityUpdate { get; set; }
    /// <summary>Gets whether the publisher has ended support.</summary>
    [JsonPropertyName("isUnsupported")]
    public bool IsUnsupported { get; init; }
    /// <summary>Gets whether the publisher has withdrawn this release.</summary>
    [JsonPropertyName("isRevoked")]
    public bool IsRevoked { get; init; }

    /// <summary>
    /// Gets the normalized version while preserving any prerelease label.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// Gets the original source tag name.
    /// </summary>
    [JsonPropertyName("tagName")]
    public string TagName { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the release is marked as a prerelease.
    /// </summary>
    [JsonPropertyName("isPrerelease")]
    public bool IsPrerelease
    {
        get; init;
    }

    /// <summary>
    /// Gets the publish time of the release.
    /// </summary>
    [JsonPropertyName("publishedAt")]
    public DateTimeOffset? PublishedAt
    {
        get; init;
    }

    /// <summary>
    /// Gets the release notes.
    /// </summary>
    [JsonPropertyName("releaseNotes")]
    public string? ReleaseNotes
    {
        get; init;
    }

    /// <summary>
    /// Gets the release page URL.
    /// </summary>
    [JsonPropertyName("releaseUrl")]
    public string? ReleaseUrl
    {
        get; init;
    }

    /// <summary>
    /// Gets all assets published for the release.
    /// </summary>
    [JsonPropertyName("assets")]
    public List<GXUpdateAsset> Assets { get; init; } = [];

    /// <summary>
    /// Gets the selected asset, or null when no asset matches.
    /// </summary>
    public GXUpdateAsset? Asset
    {
        get; init;
    }
}
