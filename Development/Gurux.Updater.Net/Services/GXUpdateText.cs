using Gurux.Updater.Enums;
using Gurux.Updater.Model;

namespace Gurux.Updater.Services;

/// <summary>Formats independent status and offered release metadata for a UI.</summary>
public static class GXUpdateText
{
    /// <summary>Formats the publisher-declared urgency and security classification.</summary>
    public static string Priority(GXUpdatePriority priority, bool security) => (priority, security) switch
    {
        (GXUpdatePriority.Critical, true) => "critical security update",
        (GXUpdatePriority.Critical, false) => "critical update",
        (GXUpdatePriority.Important, true) => "important security update",
        (GXUpdatePriority.Important, false) => "important update",
        (_, true) => "security update",
        _ => "normal update"
    };

    /// <summary>Formats the installed version status and retains available update information.</summary>
    public static string Status(GXUpdateInfo info)
    {
        string text = info.Status switch
        {
            GXUpdateStatus.UpToDate => "Up to date",
            GXUpdateStatus.UpdateAvailable => "Update available",
            GXUpdateStatus.Unsupported => "The installed version is no longer supported",
            GXUpdateStatus.Revoked => "The installed release has been revoked",
            GXUpdateStatus.CheckFailed => "Update check failed",
            _ => "Update status is unknown"
        };
        if (info.Compatibility is { IsCompatible: false } compatibility)
            text += " — " + (compatibility.IsKnown ? "Module DLL versions are incompatible" : "Module dependency information is missing");
        if (info.UpdateAvailable)
        {
            if (info.Status != GXUpdateStatus.UpdateAvailable) text += " — Update available";
            if (info.Priority != GXUpdatePriority.Normal || info.IsSecurityUpdate)
                text += " — " + Priority(info.Priority, info.IsSecurityUpdate);
        }
        return text;
    }
}
