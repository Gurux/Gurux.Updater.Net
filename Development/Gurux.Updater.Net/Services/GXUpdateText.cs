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
