using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gurux.Updater.Model;
using NuGet.Versioning;

namespace Gurux.Updater.Services;

internal static class GXLocalizationJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
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
    }

    private static void CheckDuplicateProperties(JsonElement element, string path, string source)
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

    private static void ValidateMetadata(GXLocalizationMetadata metadata, IEnumerable<string>? cultures)
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
            try
            {
                CultureInfo info = CultureInfo.GetCultureInfo(culture);
                if (!string.Equals(info.Name, culture, StringComparison.OrdinalIgnoreCase))
                {
                    throw new CultureNotFoundException();
                }
            }
            catch (CultureNotFoundException ex)
            {
                throw new InvalidDataException($"Invalid culture '{culture}'.", ex);
            }
        }
        if (supported.Count == 0 || string.IsNullOrEmpty(metadata.DefaultCulture) || !supported.Contains(metadata.DefaultCulture))
        {
            throw new InvalidDataException("defaultCulture must identify one of the package's supported cultures.");
        }
    }

    internal static void ValidatePackage(GXLocalizationPackage package)
    {
        if (package.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported localization package schemaVersion {package.SchemaVersion}.");
        }
        ValidateOwner(package.Owner, package.OwnerType);
        ValidateMetadata(package, package.Resources?.Keys);
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
            HashSet<NuGetVersion> versions = new(VersionComparer.VersionRelease);
            foreach (GXLocalizationRelease release in item.Releases)
            {
                if (release == null)
                {
                    throw new InvalidDataException($"Null release for '{item.Owner}'.");
                }
                ValidateMetadata(release, release.Cultures);
                if (!versions.Add(Version(release.Version)))
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
            !VersionComparer.VersionRelease.Equals(Version(package.Version), Version(release.Version)) ||
            package.OwnerVersionRange != release.OwnerVersionRange || package.DefaultCulture != release.DefaultCulture ||
            EffectiveDescription(package.Owner, package.OwnerType, package.Description) !=
                EffectiveDescription(item.Owner, item.OwnerType, release.Description) || package.ReleaseNotes != release.ReleaseNotes ||
            !new HashSet<string>(package.Resources.Keys, StringComparer.OrdinalIgnoreCase).SetEquals(release.Cultures))
        {
            throw new InvalidDataException($"Package metadata does not match catalog release '{item.Owner}/{release.Version}'.");
        }
    }
}
