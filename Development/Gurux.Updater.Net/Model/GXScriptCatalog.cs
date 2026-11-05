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

/// <summary>Versioned metadata for one standalone C# source file.</summary>
public sealed class GXScriptManifest
{
    /// <summary>Manifest schema, currently 1.</summary>
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;
    /// <summary>Stable script identifier.</summary>
    public string Id { get; init; } = "";
    /// <summary>Immutable NuGet version.</summary>
    public string Version { get; init; } = "";
    /// <summary>Portable .cs or .csx basename.</summary>
    public string FileName { get; init; } = "";
    /// <summary>Purpose of the script.</summary>
    public string Description { get; init; } = "";
    /// <summary>Script author.</summary>
    public string Author { get; init; } = "";
    /// <summary>License identifier or text.</summary>
    public string License { get; init; } = "";
    /// <summary>NuGet version range supported by the script; * allows all versions.</summary>
    public string AmiVersionRange { get; init; } = "";
    /// <summary>SHA-256 of the exact source bytes.</summary>
    public string Sha256 { get; init; } = "";
    /// <summary>Exact source length.</summary>
    public long Size { get; init; }
    /// <summary>Source URL relative to the script index.</summary>
    public string Path { get; set; } = "";
}

/// <summary>All retained immutable script releases.</summary>
public sealed class GXScriptCatalog
{
    /// <summary>Index schema, currently 1.</summary>
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;
    /// <summary>Versioned manifests.</summary>
    public List<GXScriptManifest> Items { get; init; } = [];
}

/// <summary>Installed provenance and original bytes used for local change rejection.</summary>
public sealed class GXInstalledScript
{
    /// <summary>The installed immutable release.</summary>
    public GXScriptManifest Manifest { get; init; } = new();
    /// <summary>Original index used to discover updates.</summary>
    public string Source { get; init; } = "";
    /// <summary>Original source bytes, encoded as base64 in JSON.</summary>
    public byte[] Original { get; init; } = [];
}
