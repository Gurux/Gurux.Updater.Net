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

namespace Gurux.Updater.Model;

/// <summary>
/// Represents a centralized update catalog.
/// </summary>
public sealed class GXUpdateCatalog
{
    /// <summary>
    /// Gets the catalog schema version.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion
    {
        get; init;
    }
    /// <summary>
    /// Gets the time when the catalog was generated.
    /// </summary>
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset? GeneratedAt
    {
        get; init;
    }
    /// <summary>
    /// Gets the catalog items.
    /// </summary>
    [JsonPropertyName("items")]
    public List<GXUpdateCatalogItem> Items { get; init; } = [];
}