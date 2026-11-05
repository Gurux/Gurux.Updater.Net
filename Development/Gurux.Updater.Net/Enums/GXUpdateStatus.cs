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

using System.Text.Json.Serialization;
namespace Gurux.Updater.Enums;
/// <summary>Publisher metadata or check outcome.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GXUpdateStatus>))]
public enum GXUpdateStatus
{
    /// <summary>Unknown.</summary>
    Unknown = 0,
    /// <summary>UpToDate.</summary>
    UpToDate = 1,
    /// <summary>UpdateAvailable.</summary>
    UpdateAvailable = 2,
    /// <summary>Unsupported.</summary>
    Unsupported = 3,
    /// <summary>Revoked.</summary>
    Revoked = 4,
    /// <summary>CheckFailed.</summary>
    CheckFailed = 5,
}
