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

/// <summary>Metadata used to register a deployable ZIP in an update catalog.</summary>
public sealed class GXCatalogPackageInfo
{
    /// <summary>Gets or sets the AMI version range declared in the package sidecar.</summary>
    public string? AmiVersionRange { get; set; }
    /// <summary>Gets or sets exact assembly dependencies read from the deployment package.</summary>
    public List<GXAssemblyReference>? References { get; set; }
    /// <summary>Gets or sets the product identifier. An empty inferred module ID requires an explicit override.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Gets or sets the optional display name.</summary>
    public string? Name { get; set; }
    /// <summary>Gets or sets the application, module or agent type.</summary>
    public CatalogProductType Type { get; set; }
    /// <summary>Gets or sets the package version.</summary>
    public string Version { get; set; } = string.Empty;
    /// <summary>Gets or sets whether the version is a prerelease.</summary>
    public bool IsPrerelease { get; set; }
}
