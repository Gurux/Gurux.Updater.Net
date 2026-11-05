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
using System.Text.Json.Serialization;

namespace Gurux.Updater.Model;

/// <summary>
/// Describes an individually downloadable device profile in the index.
/// </summary>
public sealed class GXDeviceProfileEntry
{
    /// <summary>Gets the profile serialization format: json (default) or xml.</summary>
    [JsonPropertyName("format")]
    public string Format { get; init; } = "json";

    /// <summary>
    /// Gets the permanent, index-wide unique identifier for this profile.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;
    /// <summary>
    /// Gets the display name of the profile.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
    /// <summary>
    /// Gets the HTTPS address of the downloadable profile file.
    /// </summary>
    [JsonPropertyName("location")]
    public string Location { get; init; } = string.Empty;
    /// <summary>
    /// Gets the expected SHA-256 hash of the file content as 64 hexadecimal characters.
    /// </summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;
    /// <summary>
    /// Gets the optional file size in bytes.
    /// </summary>
    [JsonPropertyName("size")]
    public long? Size
    {
        get; init;
    }
    /// <summary>
    /// Gets the optional user-visible revision label.
    /// </summary>
    [JsonPropertyName("revision")]
    public string? Revision
    {
        get; init;
    }
    /// <summary>
    /// Gets the optional selection metadata for this profile.
    /// </summary>
    [JsonPropertyName("settings")]
    public JsonElement? Settings
    {
        get; init;
    }
}
