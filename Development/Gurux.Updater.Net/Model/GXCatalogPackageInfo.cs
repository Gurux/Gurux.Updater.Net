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
