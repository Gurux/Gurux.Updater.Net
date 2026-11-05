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

/// <summary>An explicit independently versioned localization directory or resource set.</summary>
public sealed class GXContentSource : GXLocalizationSource
{
    /// <summary>localization or resources.</summary>
    public string Kind { get; set; } = "";
    /// <summary>Resource set name (localization instead uses culture).</summary>
    public string? Name { get; set; }
}

/// <summary>Immutable annotated-tag and GitHub Release manifest.</summary>
public sealed class GXContentManifest
{
    /// <summary>Module-prefixed independently versioned all-language or asset package.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ModulePackage { get; init; }
    /// <summary>Manifest protocol version.</summary>
    [JsonRequired] public int SchemaVersion { get; init; } = 1;
    /// <summary>localization or resources.</summary>
    public string Kind { get; init; } = "";
    /// <summary>Owner identifier.</summary>
    public string Owner { get; init; } = "";
    /// <summary>module or application.</summary>
    public string OwnerType { get; init; } = "";
    /// <summary>Canonical culture or resource set name.</summary>
    public string Unit { get; init; } = "";
    /// <summary>Independent NuGet version.</summary>
    public string Version { get; init; } = "";
    /// <summary>Effective compatibility bound.</summary>
    public string? OwnerVersionRange { get; init; }
    /// <summary>Canonical source content hash.</summary>
    public string ContentSha256 { get; init; } = "";
    /// <summary>Exact artifact bytes SHA-256.</summary>
    public string Sha256 { get; init; } = "";
    /// <summary>Exact artifact size.</summary>
    public long Size { get; init; }
    /// <summary>Namespace isolates content tags from binary module tags.</summary>
    public string Tag => ModulePackage ? $"{Owner}/{(Kind == "localization" ? "localization" : "assets")}/v{Version}" : $"{Kind}/{Owner}/{Unit}/v{Version}";
    /// <summary>GitHub Release asset filename.</summary>
    public string Package => ModulePackage ? $"{Owner}-{(Kind == "localization" ? "localization" : "assets")}-{Version}.{(Kind == "localization" ? "json" : "zip")}" : $"{Kind}-{Owner}-{Unit}-{Version}.{(Kind == "localization" ? "json" : "zip")}";
    /// <summary>Relative path in the kind-specific catalog directory.</summary>
    public string Path => $"packages/{Owner}/{Unit}/{Version}.{(Kind == "localization" ? "json" : "zip")}";
}

/// <summary>A package and its immutable public metadata.</summary>
public sealed record GXPreparedContent(GXContentManifest Manifest, byte[] Bytes);

/// <summary>Retained resource releases, keyed by owner, unit and version.</summary>
public sealed class GXResourceCatalog
{
    /// <summary>Supported index schema.</summary>
    [JsonRequired] public int SchemaVersion { get; init; } = 1;
    /// <summary>All immutable retained resource releases.</summary>
    [JsonRequired] public List<GXContentManifest> Items { get; init; } = [];
}
