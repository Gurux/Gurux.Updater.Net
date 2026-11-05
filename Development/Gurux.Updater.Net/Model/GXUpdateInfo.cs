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
/// Describes the installed version and the latest GitHub release of an update target.
/// </summary>
public sealed class GXUpdateInfo
{
    /// <summary>Gets the product's declared maintenance state, or null if unknown.</summary>
    public string? MaintenanceStatus { get; init; }
    /// <summary>Gets the product's category, or null if unknown.</summary>
    public string? Category { get; init; }
    /// <summary>Gets the product's audience, or null if unknown.</summary>
    public string? Audience { get; init; }
    /// <summary>Gets module dependency compatibility, or null when it was not checked.</summary>
    public GXModuleCompatibilityResult? Compatibility { get; init; }
    /// <summary>Gets the outcome for the installed version.</summary>
    public GXUpdateStatus Status { get; init; } = GXUpdateStatus.Unknown;
    /// <summary>Gets or sets the highest declared urgency in the offered update path.</summary>
    public GXUpdatePriority Priority { get; set; } = GXUpdatePriority.Normal;
    /// <summary>Gets or sets whether the offered path contains declared security fixes.</summary>
    public bool IsSecurityUpdate { get; set; }
    /// <summary>Gets failure detail, or null on success.</summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets the name of the update target.
    /// </summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>
    /// Gets the kind of update target.
    /// </summary>
    public UpdateTargetType Type { get; init; } = UpdateTargetType.Application;
    /// <summary>
    /// Gets the path to the application or add-in whose version is checked.
    /// </summary>
    public string Application { get; init; } = string.Empty;
    /// <summary>
    /// Gets the GitHub repository in owner/name format.
    /// </summary>
    public string Repository { get; init; } = string.Empty;
    /// <summary>
    /// Gets the installed version of the update target.
    /// </summary>
    public string CurrentVersion { get; init; } = string.Empty;
    /// <summary>
    /// Gets the normalized version of the latest GitHub release.
    /// </summary>
    public string LatestVersion { get; init; } = string.Empty;
    /// <summary>
    /// Gets a value indicating whether the latest release is newer than the installed version.
    /// </summary>
    public bool UpdateAvailable
    {
        get; init;
    }
    /// <summary>
    /// Gets a value indicating whether the update check is running in a detected container.
    /// </summary>
    public bool IsContainer
    {
        get; init;
    }
    /// <summary>
    /// Gets the release notes for the latest version.
    /// </summary>
    public string? ReleaseNotes
    {
        get; init;
    }
    /// <summary>
    /// Gets the URL of the latest GitHub release page.
    /// </summary>
    public string? ReleaseUrl
    {
        get; init;
    }
    /// <summary>
    /// Gets the selected release asset, or null when no asset matches.
    /// </summary>
    public GXUpdateAsset? Asset
    {
        get; init;
    }
}
