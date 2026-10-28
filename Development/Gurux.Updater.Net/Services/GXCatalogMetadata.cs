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
