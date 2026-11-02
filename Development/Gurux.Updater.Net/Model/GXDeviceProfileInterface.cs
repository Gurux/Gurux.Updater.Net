using System.Text.Json.Serialization;

namespace Gurux.Updater.Model;

/// <summary>Groups device profile variants by communication interface.</summary>
public sealed class GXDeviceProfileInterface
{
    /// <summary>Gets the interface name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the Gurux DLMS interface type value.</summary>
    [JsonPropertyName("interfaceType")]
    public int InterfaceType { get; init; }

    /// <summary>Gets the device variants and their settings.</summary>
    [JsonPropertyName("versions")]
    public List<GXDeviceProfileVersion> Versions { get; init; } = [];
}
