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
    public UpdateTargetType Type
    {
        get; init;
    }
    /// <summary>
    /// Gets the update information, or null when the check fails.
    /// </summary>
    public GXUpdateInfo? Update
    {
        get; init;
    }
    /// <summary>
    /// Gets the error message, or null when the check succeeds.
    /// </summary>
    public string? Error
    {
        get; init;
    }
    /// <summary>
    /// Gets a value indicating whether the update check completed without an error.
    /// </summary>
    public bool Succeeded => Error is null && Update?.Status != GXUpdateStatus.CheckFailed;
    /// <summary>Gets the check outcome independently of update availability.</summary>
    public GXUpdateStatus Status => Error is not null ? GXUpdateStatus.CheckFailed : Update?.Status ?? GXUpdateStatus.Unknown;
}