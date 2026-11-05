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
