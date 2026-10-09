using Gurux.Updater.Enums;

namespace Gurux.Updater.Model;

/// <summary>Metadata used to register a deployable ZIP in an update catalog.</summary>
public sealed class GXCatalogPackageInfo
{
    /// <summary>Gets or sets the product identifier. An empty inferred module ID requires an explicit override.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Gets or sets the optional display name.</summary>
    public string? Name { get; set; }
    /// <summary>Gets or sets the application or module type.</summary>
    public GXCatalogProductType Type { get; set; }
    /// <summary>Gets or sets the package version.</summary>
    public string Version { get; set; } = string.Empty;
    /// <summary>Gets or sets whether the version is a prerelease.</summary>
    public bool IsPrerelease { get; set; }
}
