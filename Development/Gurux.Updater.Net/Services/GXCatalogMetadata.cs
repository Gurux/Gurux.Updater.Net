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

using Gurux.Updater.Model;
using NuGet.Versioning;
using System.Text.RegularExpressions;

namespace Gurux.Updater.Services;

/// <summary>Validates optional catalog classification and declared AMI compatibility.</summary>
public static class GXCatalogMetadata
{
    /// <summary>Gets the publisher-defined maintenance states supported by this catalog.</summary>
    public static IReadOnlyList<string> MaintenanceStatuses { get; } = Array.AsReadOnly(new[] { "active", "minimal", "seeking-maintainer", "unsupported" });

    /// <summary>Validates a category, audience or maintenance state when supplied.</summary>
    public static void ValidateClassification(string? value, string field)
    {
        if (value is not null && (value.Length > 100 || !Regex.IsMatch(value, @"\A[a-z][a-z0-9]*(?:-[a-z0-9]+)*\z") ||
            field == "maintenanceStatus" && !MaintenanceStatuses.Contains(value)))
            throw new InvalidDataException($"Invalid {field}.");
    }

    /// <summary>Validates a NuGet AMI range, rejecting floating and empty intervals.</summary>
    public static void ValidateAmiVersionRange(string? value)
    {
        if (value is null)
            return;
        if (value.Length > 200 || !VersionRange.TryParse(value, out var range) || value != "*" && range.IsFloating ||
            range.MinVersion is not null && range.MaxVersion is not null &&
            (VersionComparer.VersionRelease.Compare(range.MinVersion, range.MaxVersion) > 0 ||
             VersionComparer.VersionRelease.Equals(range.MinVersion, range.MaxVersion) && !(range.IsMinInclusive && range.IsMaxInclusive)))
            throw new InvalidDataException("Invalid amiVersionRange.");
    }

    /// <summary>Checks a supplied AMI version against a release's declared range; null means unknown.</summary>
    public static bool? MatchesAmiVersion(GXUpdateRelease release, string? amiVersion)
    {
        ValidateAmiVersionRange(release.AmiVersionRange);
        if (release.AmiVersionRange is null || amiVersion is null)
            return null;
        if (!NuGetVersion.TryParse(amiVersion, out var version))
            throw new InvalidDataException("Invalid AMI version.");
        return VersionRange.Parse(release.AmiVersionRange).Satisfies(version);
    }
}
