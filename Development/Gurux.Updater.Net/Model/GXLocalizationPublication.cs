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

/// <summary>An explicitly registered source directory and optional project metadata overrides.</summary>
public class GXLocalizationSource
{
    /// <summary>Directory containing only culture-named translation JSON files.</summary>
    public string Directory { get; set; } = "";
    /// <summary>Optional csproj for evaluated metadata inference.</summary>
    public string? Project { get; set; }
    /// <summary>Optional deployment ZIP or NuGet package used for owner metadata inference.</summary>
    public string? MetadataPackage { get; set; }
    /// <summary>PackageId or explicit owner.</summary>
    public string? Owner { get; set; }
    /// <summary>application or module.</summary>
    public string? OwnerType { get; set; }
    /// <summary>Owner's default language, independent of selected cultures.</summary>
    public string? DefaultCulture { get; set; }
    /// <summary>Optional NuGet compatibility range, independent of translation version.</summary>
    public string? OwnerVersionRange { get; set; }
}

/// <summary>A complete request; no file paths are accepted by the server.</summary>
public sealed class GXLocalizationSnapshot
{
    /// <summary>Protocol marker distinguishing source publication from legacy package upload.</summary>
    [JsonRequired]
    public int SchemaVersion { get; init; } = 2;
    /// <summary>Versionless source packages. Version is ignored and allocated under the publication lock.</summary>
    [JsonRequired]
    public List<GXLocalizationPackage> Packages { get; init; } = [];
}

/// <summary>A language's proposed or committed release.</summary>
public sealed record GXLocalizationLanguagePlan(string Owner, string Culture, string Version, bool Changed, string ContentSha256);

/// <summary>Per-language update/status report. Updated denotes availability for status and installation for update.</summary>
public sealed record GXLocalizationUpdateResult(string Owner, string? Culture, string LocalVersion, string CatalogVersion, bool Updated, string? Error);

/// <summary>Read-only publication preview or successful publication result.</summary>
public sealed class GXLocalizationPublicationPlan
{
    /// <summary>Each registered language including unchanged languages.</summary>
    public List<GXLocalizationLanguagePlan> Languages { get; } = [];
    /// <summary>Incomplete translations and removed source cultures; history remains available.</summary>
    public List<string> Warnings { get; } = [];
}
