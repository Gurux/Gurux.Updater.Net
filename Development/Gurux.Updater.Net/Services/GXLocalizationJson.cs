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

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gurux.Updater.Model;
using NuGet.Versioning;

namespace Gurux.Updater.Services;

internal static class GXLocalizationJson
{
    private static readonly HashSet<string> KnownCultures = CultureInfo.GetCultures(CultureTypes.AllCultures)
        .Select(x => x.Name).Where(x => x.Length != 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NewLine = "\n",
        PropertyNameCaseInsensitive = false
    };

    internal static T Read<T>(byte[] bytes, string source)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            CheckDuplicateProperties(document.RootElement, "$", source);
            return document.RootElement.Deserialize<T>(Options) ?? throw new InvalidDataException($"'{source}' contains an empty JSON document.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid JSON in '{source}' at {ex.Path ?? "$"}: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException($"Invalid localization structure in '{source}': {ex.Message}", ex);
        }
    }

    internal static void CheckDuplicateProperties(JsonElement element, string path, string source)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException($"Duplicate JSON property/resource key '{property.Name}' at {path} in '{source}'. Resource keys are case-sensitive.");
                }
                CheckDuplicateProperties(property.Value, path + "." + property.Name, source);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                CheckDuplicateProperties(child, path + "[]", source);
            }
        }
    }

    internal static NuGetVersion Version(string? version)
    {
        if (!NuGetVersion.TryParse(version, out NuGetVersion? value))
        {
            throw new InvalidDataException($"Invalid localization package version '{version}'. Use a NuGet version such as 1.2.0 or 1.2.0-beta.1.");
        }
        return value;
    }

    internal static void ValidateOwner(string? owner, string? type)
    {
        if (string.IsNullOrEmpty(owner) || !Regex.IsMatch(owner, @"\A[A-Za-z0-9][A-Za-z0-9._-]*\z") || owner.EndsWith('.') ||
            Regex.IsMatch(owner.Split('.')[0], @"\A(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])\z", RegexOptions.IgnoreCase))
        {
            throw new InvalidDataException($"Invalid owner identifier '{owner}'. Use a safe filename containing letters, digits, dots, underscores or hyphens.");
        }
        if (type is not ("application" or "module"))
        {
            throw new InvalidDataException($"Invalid owner type '{type}' for '{owner}'; expected application or module.");
        }
    }

    internal static string Culture(string culture)
    {
        if (string.IsNullOrWhiteSpace(culture) || !Regex.IsMatch(culture, @"\A[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*\z"))
            throw new InvalidDataException($"Invalid culture '{culture}'.");
        try
        {
            var info = CultureInfo.GetCultureInfo(culture);
            if (!KnownCultures.Contains(info.Name)) throw new CultureNotFoundException();
            return info.Name;
        }
        catch (CultureNotFoundException ex) { throw new InvalidDataException($"Invalid culture '{culture}'.", ex); }
    }

    private static void ValidateMetadata(GXLocalizationMetadata metadata, IEnumerable<string>? cultures, bool single = false)
    {
        _ = Version(metadata.Version);
        if (metadata.OwnerVersionRange != null &&
            (!VersionRange.TryParse(metadata.OwnerVersionRange, out VersionRange? range) || range.IsFloating))
        {
            throw new InvalidDataException($"Invalid ownerVersionRange '{metadata.OwnerVersionRange}'. Use a fixed NuGet range such as [1.0.0,2.0.0).");
        }
        HashSet<string> supported = new(StringComparer.OrdinalIgnoreCase);
        foreach (string culture in cultures ?? throw new InvalidDataException("Supported cultures are required."))
        {
            if (string.IsNullOrWhiteSpace(culture) || !supported.Add(culture))
            {
                throw new InvalidDataException($"Empty or duplicate culture '{culture}'.");
            }
            _ = Culture(culture);
        }
        if (supported.Count == 0 || string.IsNullOrEmpty(metadata.DefaultCulture) || !single && !supported.Contains(metadata.DefaultCulture))
        {
            throw new InvalidDataException("defaultCulture must identify one of the package's supported cultures.");
        }
        _ = Culture(metadata.DefaultCulture);
    }

    internal static void ValidatePackage(GXLocalizationPackage package)
    {
        if (package.SchemaVersion is not (1 or 2))
        {
            throw new InvalidDataException($"Unsupported localization package schemaVersion {package.SchemaVersion}.");
        }
        ValidateOwner(package.Owner, package.OwnerType);
        ValidateMetadata(package, package.Resources?.Keys, package.SchemaVersion == 2);
        if (package.SchemaVersion == 2 && (package.Culture == null || package.Resources!.Count != 1 ||
            !package.Resources.ContainsKey(package.Culture) || Culture(package.Culture) != package.Culture) || package.SchemaVersion == 1 && package.Culture != null)
            throw new InvalidDataException("Schema 2 requires exactly one canonical culture; schema 1 cannot declare culture.");
        foreach (var culture in package.Resources!)
        {
            if (culture.Value == null)
            {
                throw new InvalidDataException($"Resources for culture '{culture.Key}' must be a JSON object.");
            }
            foreach (var resource in culture.Value)
            {
                if (string.IsNullOrWhiteSpace(resource.Key) || resource.Value == null)
                {
                    throw new InvalidDataException($"Resource '{culture.Key}/{resource.Key}' must have a nonempty key and a string value.");
                }
            }
        }
    }

    internal static void ValidateCatalog(GXLocalizationCatalog catalog)
    {
        if (catalog.SchemaVersion != 1 || catalog.Items == null)
        {
            throw new InvalidDataException("Localization catalog must use schemaVersion 1 and contain an items array.");
        }
        // Prevent case aliases from colliding when a checkout is used on Windows.
        HashSet<string> owners = new(StringComparer.OrdinalIgnoreCase);
        foreach (GXLocalizationCatalogItem item in catalog.Items)
        {
            if (item == null)
            {
                throw new InvalidDataException("Catalog items cannot be null.");
            }
            ValidateOwner(item.Owner, item.OwnerType);
            if (!owners.Add(item.Owner) || item.Releases == null || item.Releases.Count == 0)
            {
                throw new InvalidDataException($"Duplicate owner '{item.Owner}' or missing releases.");
            }
            HashSet<string> versions = new(StringComparer.OrdinalIgnoreCase);
            foreach (GXLocalizationRelease release in item.Releases)
            {
                if (release == null)
                {
                    throw new InvalidDataException($"Null release for '{item.Owner}'.");
                }
                ValidateMetadata(release, release.Cultures, release.Culture != null);
                if (release.Culture != null && (Culture(release.Culture) != release.Culture || release.Cultures.Count != 1 || release.Cultures[0] != release.Culture ||
                    release.ContentSha256 == null || !Regex.IsMatch(release.ContentSha256, @"\A[0-9a-fA-F]{64}\z")))
                    throw new InvalidDataException("Single-culture release requires canonical culture, cultures and contentSha256.");
                if (!versions.Add((release.Culture ?? "") + "/" + Version(release.Version).ToNormalizedString().Split('+')[0]))
                {
                    throw new InvalidDataException($"Duplicate localization version '{item.Owner}/{release.Version}'.");
                }
                if (string.IsNullOrWhiteSpace(release.Path) || release.Size <= 0 ||
                    release.Sha256 == null || !Regex.IsMatch(release.Sha256, @"\A[0-9a-fA-F]{64}\z"))
                {
                    throw new InvalidDataException($"Release '{item.Owner}/{release.Version}' requires a package path, positive size and 64-character SHA-256.");
                }
            }
        }
    }

    internal static string EffectiveDescription(string owner, string type, string? description)
        => string.IsNullOrWhiteSpace(description) ? $"Localized resources for {owner} {type}" : description;

    internal static void MatchPackage(GXLocalizationPackage package, GXLocalizationCatalogItem item, GXLocalizationRelease release)
    {
        ValidatePackage(package);
        if (package.Owner != item.Owner || package.OwnerType != item.OwnerType ||
            package.Culture != release.Culture ||
            !VersionComparer.VersionRelease.Equals(Version(package.Version), Version(release.Version)) ||
            package.OwnerVersionRange != release.OwnerVersionRange || package.DefaultCulture != release.DefaultCulture ||
            EffectiveDescription(package.Owner, package.OwnerType, package.Description) !=
                EffectiveDescription(item.Owner, item.OwnerType, release.Description) || package.ReleaseNotes != release.ReleaseNotes ||
            !new HashSet<string>(package.Resources.Keys, StringComparer.OrdinalIgnoreCase).SetEquals(release.Cultures))
        {
            throw new InvalidDataException($"Package metadata does not match catalog release '{item.Owner}/{release.Version}'.");
        }
        if (release.ContentSha256 != null && GXLocalizationPublisher.ContentHash(package) != release.ContentSha256.ToLowerInvariant())
            throw new InvalidDataException("Package contentSha256 does not match the index.");
    }
}
