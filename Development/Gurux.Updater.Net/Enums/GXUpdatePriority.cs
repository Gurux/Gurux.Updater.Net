using System.Text.Json.Serialization;
namespace Gurux.Updater.Enums;
/// <summary>Publisher metadata or check outcome.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GXUpdatePriority>))]
public enum GXUpdatePriority
{
    /// <summary>Normal.</summary>
    Normal = 0,
    /// <summary>Important.</summary>
    Important = 1,
    /// <summary>Critical.</summary>
    Critical = 2,
}
