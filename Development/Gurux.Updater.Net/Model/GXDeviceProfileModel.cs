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

using System.Text.Json.Serialization;

namespace Gurux.Updater.Model;

/// <summary>
/// Represents a device model in the device profile index.
/// </summary>
public sealed class GXDeviceProfileModel
{
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