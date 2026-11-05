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
/// Represents a device model in the device profile index.
/// </summary>
public sealed class GXDeviceProfileModel
{
    /// <summary>Gets the optional model image.</summary>
    [JsonPropertyName("image")]
    public GXDeviceProfileImage? Image { get; init; }

    /// <summary>Gets interface-specific profiles in schema version 2.</summary>
    [JsonPropertyName("interfaces")]
    public List<GXDeviceProfileInterface> Interfaces { get; init; } = [];

    /// <summary>
    /// Gets the display name of the model.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
    /// <summary>
    /// Gets profile entries that apply to this model without a specific device version.
    /// These entries are independent of any entry in <see cref="Versions"/> and are
    /// never inherited by version-specific profiles.
    /// </summary>
    [JsonPropertyName("settings")]
    public List<GXDeviceProfileEntry> Settings { get; init; } = [];
    /// <summary>
    /// Gets the device versions or variants for this model.
    /// Each version maintains its own independent profile list.
    /// </summary>
    [JsonPropertyName("versions")]
    public List<GXDeviceProfileVersion> Versions { get; init; } = [];
}
