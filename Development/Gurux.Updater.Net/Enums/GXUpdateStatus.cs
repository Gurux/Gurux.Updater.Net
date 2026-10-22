using System.Text.Json.Serialization;
namespace Gurux.Updater.Enums;
/// <summary>Publisher metadata or check outcome.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GXUpdateStatus>))]
public enum GXUpdateStatus
{
    /// <summary>Unknown.</summary>
    Unknown = 0,
    /// <summary>UpToDate.</summary>
    UpToDate = 1,
    /// <summary>UpdateAvailable.</summary>
    UpdateAvailable = 2,
    /// <summary>Unsupported.</summary>
    Unsupported = 3,
    /// <summary>Revoked.</summary>
    Revoked = 4,
    /// <summary>CheckFailed.</summary>
    CheckFailed = 5,
}
