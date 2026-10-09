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