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
/// Contains the update information or error for one target in a batch check.
/// </summary>
public sealed class GXUpdateCheckResult
{
    /// <summary>
    /// Gets the name of the update target.
    /// </summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>
    /// Gets the kind of update target.
    /// </summary>
    public UpdateTargetType Type { get; init; }
    /// <summary>
    /// Gets the update information, or null when the check fails.
    /// </summary>
    public GXUpdateInfo? Update { get; init; }
    /// <summary>
    /// Gets the error message, or null when the check succeeds.
    /// </summary>
    public string? Error { get; init; }
    /// <summary>
    /// Gets a value indicating whether the update check completed without an error.
    /// </summary>
    public bool Succeeded => Error is null;
}