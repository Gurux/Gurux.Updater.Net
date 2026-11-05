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

using Gurux.Updater.Enums;

namespace Gurux.Updater.Model;

/// <summary>
/// Specifies an application or add-in to check for updates.
/// </summary>
public sealed class GXUpdateTarget
{
    /// <summary>Gets the installed AMI product version for declared range checks.</summary>
    public string? AmiVersion { get; init; }
    /// <summary>Gets host/runtime DLL basenames and assembly versions for module compatibility checks.
    /// Null leaves compatibility unchecked; an empty collection means no dependencies are available.</summary>
    public IReadOnlyDictionary<string, Version>? AvailableAssemblies { get; init; }
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
