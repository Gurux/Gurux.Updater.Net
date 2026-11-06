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

using System.Reflection;
using System.Text.RegularExpressions;
using Gurux.Updater.Model;

namespace Gurux.Updater.Services;

/// <summary>Checks exact assembly versions without loading or executing assemblies.</summary>
public static class GXModuleCompatibility
{
    /// <summary>Checks a release against the host's available DLL basenames and assembly versions.</summary>
    public static GXModuleCompatibilityResult Check(GXUpdateRelease release, IReadOnlyDictionary<string, Version> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        return Check(release, assemblies, null);
    }

    /// <summary>Checks the declared AMI range and optionally the host DLL versions.</summary>
    public static GXModuleCompatibilityResult Check(GXUpdateRelease release, IReadOnlyDictionary<string, Version>? assemblies, string? amiVersion)
    {
        ArgumentNullException.ThrowIfNull(release);
        bool? amiMatches = GXCatalogMetadata.MatchesAmiVersion(release, amiVersion);
        if (release.References is not null)
        {
            ValidateReferences(release.References);
        }
        var available = new Dictionary<string, Version>(assemblies ?? new Dictionary<string, Version>(), StringComparer.OrdinalIgnoreCase);
        List<GXModuleCompatibilityIssue> issues = [];
        foreach (var reference in assemblies is null ? [] : release.References ?? [])
        {
            var required = Version.Parse(reference.Version);
            available.TryGetValue(reference.Name, out var installed);
            if (required != installed)
            {
                issues.Add(new() { Name = reference.Name, RequiredVersion = reference.Version, InstalledVersion = installed?.ToString() });
            }
        }
        bool known = (assemblies is null ? amiMatches is not null : release.References is not null) &&
            (release.AmiVersionRange is null || amiMatches is not null);
        return new() { IsKnown = known, Issues = issues, AmiVersionRange = release.AmiVersionRange,
            IsAmiVersionCompatible = amiMatches };
    }

    internal static void ValidateReferences(IEnumerable<GXAssemblyReference> references)
    {
        foreach (var reference in references)
        {
            if (reference is null || reference.Name is null || !Regex.IsMatch(reference.Name, @"\A[A-Za-z0-9_][A-Za-z0-9_.-]*\.dll\z", RegexOptions.IgnoreCase) ||
                !Version.TryParse(reference.Version, out var version) || version.Build < 0 || version.Revision < 0 ||
                version.Major > 65535 || version.Minor > 65535 || version.Build > 65535 || version.Revision > 65535)
            {
                throw new InvalidDataException("Assembly references require a DLL basename and four-part assembly version.");
            }
        }
    }

    /// <summary>Reads assembly versions from explicit host/runtime DLL paths; native DLLs are skipped.</summary>
    public static IReadOnlyDictionary<string, Version> ReadAssemblyVersions(IEnumerable<string> assemblyPaths)
    {
        ArgumentNullException.ThrowIfNull(assemblyPaths);
        var result = new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in assemblyPaths)
        {
            try
            {
                var assembly = AssemblyName.GetAssemblyName(path);
                string name = assembly.Name + ".dll";
                if (result.TryGetValue(name, out var previous) && previous != assembly.Version)
                {
                    throw new InvalidDataException($"Conflicting installed assembly versions for '{name}'.");
                }
                result[name] = assembly.Version!;
            }
            catch (BadImageFormatException) { }
        }
        return result;
    }
}
