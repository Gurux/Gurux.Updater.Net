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
    public string Tag => $"{Kind}/{Owner}/{Unit}/v{Version}";
    /// <summary>GitHub Release asset filename.</summary>
    public string Package => $"{Kind}-{Owner}-{Unit}-{Version}.{(Kind == "localization" ? "json" : "zip")}";
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
