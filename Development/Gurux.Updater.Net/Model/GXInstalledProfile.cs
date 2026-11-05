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

/// <summary>
/// Describes a device profile that has been installed by the host application.
/// </summary>
public sealed class GXInstalledProfile
{
    /// <summary>
    /// Gets the permanent unique identifier of the installed profile.
    /// </summary>
    public string Id { get; init; } = string.Empty;
    /// <summary>
    /// Gets the SHA-256 hash of the installed file content as 64 hexadecimal characters.
    /// </summary>
    public string Sha256 { get; init; } = string.Empty;
}