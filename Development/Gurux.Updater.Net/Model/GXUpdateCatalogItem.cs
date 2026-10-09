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
using System.Text.Json.Serialization;

namespace Gurux.Updater.Model;

/// <summary>
/// Represents a product entry in the update catalog.
/// </summary>
public sealed class GXUpdateCatalogItem
{
    /// <summary>
    /// Gets the unique product identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;
    /// <summary>
    /// Gets the catalog product type.
    /// </summary>
    [JsonPropertyName("type")]
    public GXCatalogProductType Type
    {
        get; init;
    }
    /// <summary>
    /// Gets the display name of the product.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name
    {
        get; init;
    }
    /// <summary>
    /// Gets the product description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description
    {
        get; init;
    }
    /// <summary>
    /// Gets the backing GitHub repository in owner/name format.
    /// </summary>
    [JsonPropertyName("repository")]
    public string? Repository
    {
        get; init;
    }
    /// <summary>
    /// Gets the releases selected for this catalog item.
    /// </summary>
    [JsonPropertyName("releases")]
    public List<GXUpdateRelease> Releases { get; init; } = [];
}