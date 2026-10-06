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
/// Represents the root of a device profile index document.
/// </summary>
public sealed class GXDeviceProfileIndex
{
    /// <summary>
    /// Gets the schema version of the index document.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }
    /// <summary>
    /// Gets the time when the index was generated.
    /// </summary>
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset? GeneratedAt { get; init; }
    /// <summary>
    /// Gets the manufacturer entries in the index.
    /// </summary>
    [JsonPropertyName("manufacturers")]
    public List<GXDeviceProfileManufacturer> Manufacturers { get; init; } = [];
}