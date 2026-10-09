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
/// Describes the update status of an installed device profile compared with the index.
/// </summary>
public sealed class GXDeviceProfileStatus
{
    /// <summary>
    /// Gets the identifier of the installed profile.
    /// </summary>
    public string Id { get; init; } = string.Empty;
    /// <summary>
    /// Gets the comparison result.
    /// </summary>
    public GXDeviceProfileStatusKind Kind
    {
        get; init;
    }
    /// <summary>
    /// Gets the SHA-256 of the installed file as reported by the host application.
    /// </summary>
    public string InstalledSha256 { get; init; } = string.Empty;
    /// <summary>
    /// Gets the corresponding index entry, or null when the profile is not in the index.
    /// </summary>
    public GXDeviceProfileEntry? Entry
    {
        get; init;
    }
}