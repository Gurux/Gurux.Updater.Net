using System.Text.Json.Serialization;

namespace Gurux.Updater.Model;

/// <summary>Describes an optional manufacturer logo or device model image.</summary>
public sealed class GXDeviceProfileImage
{
    /// <summary>Gets the absolute HTTPS download URL.</summary>
    [JsonPropertyName("location")]
    public string Location { get; init; } = string.Empty;

    /// <summary>Gets the hexadecimal SHA-256 of the image bytes.</summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>Gets the expected file size in bytes.</summary>
    [JsonPropertyName("size")]
    public long Size { get; init; }
}
