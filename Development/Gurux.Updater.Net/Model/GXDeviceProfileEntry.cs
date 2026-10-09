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
using System.Text.Json.Serialization;

namespace Gurux.Updater.Model;

/// <summary>
/// Describes an individually downloadable device profile in the index.
/// </summary>
public sealed class GXDeviceProfileEntry
{
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