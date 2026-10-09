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
/// Specifies an application or add-in to check for updates.
/// </summary>
public sealed class GXUpdateTarget
{
    /// <summary>
    /// Gets the name of the update target.
    /// </summary>
    public string? Name { get; init; } = default!;
    /// <summary>
    /// Gets the kind of update target.
    /// </summary>
    public UpdateTargetType Type { get; init; } = UpdateTargetType.AddIn;
    /// <summary>
    /// Gets the path to the application or add-in whose version is checked.
    /// </summary>
    public string? Application { get; init; } = default!;

    /// <summary>
    /// Gets the explicit current version, which takes precedence over the application file version.
    /// </summary>
    public string? CurrentVersion
    {
        get; init;
    }

    /// <summary>
    /// Gets the GitHub repository in owner/name format.
    /// </summary>
    public string? Repository { get; init; } = default!;
    /// <summary>
    /// Gets the case-insensitive asset name pattern, whose parts are separated by asterisks.
    /// </summary>
    public string? AssetPattern
    {
        get; init;
    }
}