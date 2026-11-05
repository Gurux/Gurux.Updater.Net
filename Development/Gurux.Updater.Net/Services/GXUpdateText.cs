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
using Gurux.Updater.Model;

namespace Gurux.Updater.Services;

/// <summary>Formats independent status and offered release metadata for a UI.</summary>
public static class GXUpdateText
{
    /// <summary>Formats the publisher-declared urgency and security classification.</summary>
    public static string Priority(GXUpdatePriority priority, bool security) => (priority, security) switch
    {
        (GXUpdatePriority.Critical, true) => "critical security update",
        (GXUpdatePriority.Critical, false) => "critical update",
        (GXUpdatePriority.Important, true) => "important security update",
        (GXUpdatePriority.Important, false) => "important update",
        (_, true) => "security update",
        _ => "normal update"
    };

    /// <summary>Formats the installed version status and retains available update information.</summary>
    public static string Status(GXUpdateInfo info)
    {
        string text = info.Status switch
        {
            GXUpdateStatus.UpToDate => "Up to date",
            GXUpdateStatus.UpdateAvailable => "Update available",
            GXUpdateStatus.Unsupported => "The installed version is no longer supported",
            GXUpdateStatus.Revoked => "The installed release has been revoked",
            GXUpdateStatus.CheckFailed => "Update check failed",
            _ => "Update status is unknown"
        };
        if (info.Compatibility is { IsCompatible: false } compatibility)
            text += " — " + (compatibility.IsAmiVersionCompatible == false ? "AMI version is outside the module's supported range" :
                compatibility.AmiVersionRange is not null && compatibility.IsAmiVersionCompatible is null ? "AMI version is required to check module compatibility" :
                compatibility.IsKnown ? "Module DLL versions are incompatible" : "Module dependency information is missing");
        if (info.UpdateAvailable)
        {
            if (info.Status != GXUpdateStatus.UpdateAvailable) text += " — Update available";
            if (info.Priority != GXUpdatePriority.Normal || info.IsSecurityUpdate)
                text += " — " + Priority(info.Priority, info.IsSecurityUpdate);
        }
        if (info.MaintenanceStatus == "unsupported") text += " — Project is unsupported";
        return text;
    }
}
