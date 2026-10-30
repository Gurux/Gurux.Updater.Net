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

/// <summary>A localization index usable from a local checkout or an HTTPS website.</summary>
public sealed class GXLocalizationCatalog
{
    /// <summary>The supported index schema version.</summary>
    [JsonRequired, JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;
    /// <summary>The last index publication time.</summary>
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset? GeneratedAt { get; set; }
    /// <summary>Applications and modules with their retained package versions.</summary>
    [JsonRequired, JsonPropertyName("items")]
    public List<GXLocalizationCatalogItem> Items { get; init; } = [];
}

/// <summary>An application or module owning localization resources.</summary>
public sealed class GXLocalizationCatalogItem
{
    /// <summary>The case-sensitive owner identifier.</summary>
    [JsonPropertyName("owner")]
    public string Owner { get; init; } = string.Empty;
    /// <summary>Either application or module.</summary>
    [JsonPropertyName("ownerType")]
    public string OwnerType { get; init; } = string.Empty;
    /// <summary>A description of the owner.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }
    /// <summary>All retained localization releases.</summary>
    [JsonPropertyName("releases")]
    public List<GXLocalizationRelease> Releases { get; init; } = [];
}

/// <summary>Metadata shared by a complete language package and its index entry.</summary>
public class GXLocalizationMetadata
{
    /// <summary>The NuGet package version.</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;
    /// <summary>An optional NuGet version range for the application or module.</summary>
    [JsonPropertyName("ownerVersionRange")]
    public string? OwnerVersionRange { get; init; }
    /// <summary>The default culture included in the package.</summary>
    [JsonPropertyName("defaultCulture")]
    public string DefaultCulture { get; init; } = string.Empty;
    /// <summary>The package description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }
    /// <summary>Optional release notes.</summary>
    [JsonPropertyName("releaseNotes")]
    public string? ReleaseNotes { get; init; }
}

/// <summary>One immutable package release referenced by an index.</summary>
public sealed class GXLocalizationRelease : GXLocalizationMetadata
{
    /// <summary>Single culture for schema 2; null identifies a legacy all-language release.</summary>
    [JsonPropertyName("culture"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Culture { get; init; }
    /// <summary>Deterministic hash of resources and effective metadata, excluding version.</summary>
    [JsonPropertyName("contentSha256"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContentSha256 { get; init; }
    /// <summary>Supported culture names.</summary>
    [JsonPropertyName("cultures")]
    public List<string> Cultures { get; init; } = [];
    /// <summary>A relative package path or absolute HTTPS address.</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;
    /// <summary>The package size in bytes.</summary>
    [JsonPropertyName("size")]
    public long Size { get; init; }
    /// <summary>The hexadecimal SHA-256 of the exact JSON file.</summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;
}

/// <summary>A complete source snapshot of all languages for one owner.</summary>
[JsonConverter(typeof(Gurux.Updater.Services.GXLocalizationPackageConverter))]
public sealed class GXLocalizationPackage : GXLocalizationMetadata
{
    /// <summary>Single culture for schema 2. Resources retain the library's culture map representation.</summary>
    [JsonPropertyName("culture")]
    public string? Culture { get; init; }
    /// <summary>The supported package schema version.</summary>
    [JsonRequired, JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;
    /// <summary>The case-sensitive owner identifier.</summary>
    [JsonPropertyName("owner")]
    public string Owner { get; init; } = string.Empty;
    /// <summary>Either application or module.</summary>
    [JsonPropertyName("ownerType")]
    public string OwnerType { get; init; } = string.Empty;
    /// <summary>Culture to case-sensitive resource key to translated value.</summary>
    [JsonPropertyName("resources")]
    public Dictionary<string, Dictionary<string, string>> Resources { get; init; } = new(StringComparer.Ordinal);
}
