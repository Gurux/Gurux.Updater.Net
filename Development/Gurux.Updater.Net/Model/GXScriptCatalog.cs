using System.Text.Json.Serialization;

namespace Gurux.Updater.Model;

/// <summary>Versioned metadata for one standalone C# source file.</summary>
public sealed class GXScriptManifest
{
    /// <summary>Manifest schema, currently 1.</summary>
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;
    /// <summary>Stable script identifier.</summary>
    public string Id { get; init; } = "";
    /// <summary>Immutable NuGet version.</summary>
    public string Version { get; init; } = "";
    /// <summary>Portable .cs or .csx basename.</summary>
    public string FileName { get; init; } = "";
    /// <summary>Purpose of the script.</summary>
    public string Description { get; init; } = "";
    /// <summary>Script author.</summary>
    public string Author { get; init; } = "";
    /// <summary>License identifier or text.</summary>
    public string License { get; init; } = "";
    /// <summary>NuGet version range supported by the script; * allows all versions.</summary>
    public string AmiVersionRange { get; init; } = "";
    /// <summary>SHA-256 of the exact source bytes.</summary>
    public string Sha256 { get; init; } = "";
    /// <summary>Exact source length.</summary>
    public long Size { get; init; }
    /// <summary>Source URL relative to the script index.</summary>
    public string Path { get; set; } = "";
}

/// <summary>All retained immutable script releases.</summary>
public sealed class GXScriptCatalog
{
    /// <summary>Index schema, currently 1.</summary>
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;
    /// <summary>Versioned manifests.</summary>
    public List<GXScriptManifest> Items { get; init; } = [];
}

/// <summary>Installed provenance and original bytes used for local change rejection.</summary>
public sealed class GXInstalledScript
{
    /// <summary>The installed immutable release.</summary>
    public GXScriptManifest Manifest { get; init; } = new();
    /// <summary>Original index used to discover updates.</summary>
    public string Source { get; init; } = "";
    /// <summary>Original source bytes, encoded as base64 in JSON.</summary>
    public byte[] Original { get; init; } = [];
}
