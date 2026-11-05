// --------------------------------------------------------------------------
// Gurux Ltd
//
// Copyright (c) Gurux Ltd.
//
// This file is part of Gurux Device Framework.
//
// Commit: $Format:%H$
// Date:   $Format:%cI$
//
// This software is available under either of the following licenses:
//
// 1. GNU General Public License, version 2 only (GPL-2.0-only).
//    See the LICENSE file included with this distribution.
//
// 2. A commercial license granted by Gurux Ltd under a separate written
//    agreement. Use under this option requires a valid commercial license
//    and is governed by the terms of that agreement.
//
// If you have not obtained a commercial license from Gurux Ltd,
// use of this software is governed by GPL-2.0-only.
//
// Commercial licensing: https://www.gurux.fi/
//
// SPDX-License-Identifier: GPL-2.0-only OR LicenseRef-Gurux-Commercial
// --------------------------------------------------------------------------

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
