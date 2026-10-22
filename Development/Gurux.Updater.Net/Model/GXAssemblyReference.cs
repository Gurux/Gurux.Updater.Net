using System.Text.Json.Serialization;

namespace Gurux.Updater.Model;

/// <summary>An assembly dependency and its exact assembly version (not its package version).</summary>
public sealed class GXAssemblyReference
{
    /// <summary>Gets the DLL basename.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets the required four-part assembly version.</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;
}
