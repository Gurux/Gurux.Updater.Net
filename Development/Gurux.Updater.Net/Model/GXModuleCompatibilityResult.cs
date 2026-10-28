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

namespace Gurux.Updater.Model;

/// <summary>Version compatibility only; does not guarantee binary or behavioral compatibility.</summary>
public sealed class GXModuleCompatibilityResult
{
    /// <summary>Gets the declared AMI range, or null if unavailable.</summary>
    public string? AmiVersionRange { get; init; }
    /// <summary>Gets whether the supplied AMI version matches; null means unchecked.</summary>
    public bool? IsAmiVersionCompatible { get; init; }
    /// <summary>Gets whether metadata and host versions are available for every requested check.</summary>
    public bool IsKnown { get; init; }
    /// <summary>Gets whether all required assembly versions match.</summary>
    public bool IsCompatible => IsKnown && Issues.Count == 0 && IsAmiVersionCompatible != false;
    /// <summary>Gets missing or mismatched dependencies.</summary>
    public IReadOnlyList<GXModuleCompatibilityIssue> Issues { get; init; } = [];
}

/// <summary>A missing or mismatched assembly dependency.</summary>
public sealed class GXModuleCompatibilityIssue
{
    /// <summary>Gets the required DLL name.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets the required assembly version.</summary>
    public string RequiredVersion { get; init; } = string.Empty;
    /// <summary>Gets the installed assembly version, or null if missing.</summary>
    public string? InstalledVersion { get; init; }
}
