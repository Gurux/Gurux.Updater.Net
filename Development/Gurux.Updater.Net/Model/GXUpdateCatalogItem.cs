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
using System.Text.Json.Serialization;

namespace Gurux.Updater.Model;

/// <summary>
/// Represents a product entry in the update catalog.
/// </summary>
public sealed class GXUpdateCatalogItem
{
    /// <summary>Gets the optional product icon URL, including its content cache key.</summary>
    [JsonPropertyName("iconUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IconUrl { get; init; }

    /// <summary>Gets the publisher-declared maintenance state; null means unknown.</summary>
    [JsonPropertyName("maintenanceStatus")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MaintenanceStatus { get; init; }
    /// <summary>Gets the product's functional category slug.</summary>
    [JsonPropertyName("category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; init; }
    /// <summary>Gets the intended audience slug, for example developer or operator.</summary>
    [JsonPropertyName("audience")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Audience { get; init; }
    /// <summary>
    /// Gets the unique product identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;
    /// <summary>
    /// Gets the catalog product type.
    /// </summary>
    [JsonPropertyName("type")]
    public CatalogProductType Type
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
