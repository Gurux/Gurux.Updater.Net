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

using Gurux.Updater.Enums;

namespace Gurux.Updater.Model;

/// <summary>
/// Describes the installed version and the latest GitHub release of an update target.
/// </summary>
public sealed class GXUpdateInfo
{
    /// <summary>
    /// Gets the name of the update target.
    /// </summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>
    /// Gets the kind of update target.
    /// </summary>
    public UpdateTargetType Type { get; init; } = UpdateTargetType.Application;
    /// <summary>
    /// Gets the path to the application or add-in whose version is checked.
    /// </summary>
    public string Application { get; init; } = string.Empty;
    /// <summary>
    /// Gets the GitHub repository in owner/name format.
    /// </summary>
    public string Repository { get; init; } = string.Empty;
    /// <summary>
    /// Gets the installed version of the update target.
    /// </summary>
    public string CurrentVersion { get; init; } = string.Empty;
    /// <summary>
    /// Gets the normalized version of the latest GitHub release.
    /// </summary>
    public string LatestVersion { get; init; } = string.Empty;
    /// <summary>
    /// Gets a value indicating whether the latest release is newer than the installed version.
    /// </summary>
    public bool UpdateAvailable
    {
        get; init;
    }
    /// <summary>
    /// Gets a value indicating whether the update check is running in a detected container.
    /// </summary>
    public bool IsContainer
    {
        get; init;
    }
    /// <summary>
    /// Gets the release notes for the latest version.
    /// </summary>
    public string? ReleaseNotes
    {
        get; init;
    }
    /// <summary>
    /// Gets the URL of the latest GitHub release page.
    /// </summary>
    public string? ReleaseUrl
    {
        get; init;
    }
    /// <summary>
    /// Gets the selected release asset, or null when no asset matches.
    /// </summary>
    public GXUpdateAsset? Asset
    {
        get; init;
    }
}
