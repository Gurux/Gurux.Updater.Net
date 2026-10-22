namespace Gurux.Updater.Model;

/// <summary>Version compatibility only; does not guarantee binary or behavioral compatibility.</summary>
public sealed class GXModuleCompatibilityResult
{
    /// <summary>Gets whether dependency metadata was supplied.</summary>
    public bool IsKnown { get; init; }
    /// <summary>Gets whether all required assembly versions match.</summary>
    public bool IsCompatible => IsKnown && Issues.Count == 0;
    /// <summary>Gets missing or mismatched dependencies.</summary>
    public IReadOnlyList<GXModuleCompatibilityIssue> Issues { get; init; } = [];
}

/// <summary>A missing or mismatched assembly dependency.</summary>
public sealed class GXModuleCompatibilityIssue
{
    /// <summary>Gets the required DLL name.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets the required assembly version.</summary>
    public string RequiredVersion { get; init; } = string.Empty;
    /// <summary>Gets the installed assembly version, or null if missing.</summary>
    public string? InstalledVersion { get; init; }
}
