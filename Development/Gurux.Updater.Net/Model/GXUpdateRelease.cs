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
/// Describes a release available for an update target.
/// </summary>
public sealed class GXUpdateRelease
{
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
    public bool IsPrerelease { get; init; }

    /// <summary>
    /// Gets the publish time of the release.
    /// </summary>
    [JsonPropertyName("publishedAt")]
    public DateTimeOffset? PublishedAt { get; init; }
 
    /// <summary>
    /// Gets the release notes.
    /// </summary>
    [JsonPropertyName("releaseNotes")]
    public string? ReleaseNotes { get; init; }
  
    /// <summary>
    /// Gets the release page URL.
    /// </summary>
    [JsonPropertyName("releaseUrl")]
    public string? ReleaseUrl { get; init; }
   
    /// <summary>
    /// Gets all assets published for the release.
    /// </summary>
    [JsonPropertyName("assets")]
    public List<GXUpdateAsset> Assets { get; init; } = [];
  
    /// <summary>
    /// Gets the selected asset, or null when no asset matches.
    /// </summary>
    public GXUpdateAsset? Asset { get; init; }
}