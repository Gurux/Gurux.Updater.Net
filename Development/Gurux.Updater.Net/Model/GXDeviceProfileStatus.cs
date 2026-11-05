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