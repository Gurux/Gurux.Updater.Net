using System.Reflection;
using System.Text.RegularExpressions;
using Gurux.Updater.Model;

namespace Gurux.Updater.Services;

/// <summary>Checks exact assembly versions without loading or executing assemblies.</summary>
public static class GXModuleCompatibility
{
    /// <summary>Checks a release against the host's available DLL basenames and assembly versions.</summary>
    public static GXModuleCompatibilityResult Check(GXUpdateRelease release, IReadOnlyDictionary<string, Version> assemblies)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(assemblies);
        if (release.References is null) return new();
        ValidateReferences(release.References);
        var available = new Dictionary<string, Version>(assemblies, StringComparer.OrdinalIgnoreCase);
        List<GXModuleCompatibilityIssue> issues = [];
        foreach (var reference in release.References)
        {
            var required = Version.Parse(reference.Version);
            available.TryGetValue(reference.Name, out var installed);
            if (required != installed)
                issues.Add(new() { Name = reference.Name, RequiredVersion = reference.Version, InstalledVersion = installed?.ToString() });
        }
        return new() { IsKnown = true, Issues = issues };
    }

    internal static void ValidateReferences(IEnumerable<GXAssemblyReference> references)
    {
        foreach (var reference in references)
        {
            if (reference is null || reference.Name is null || !Regex.IsMatch(reference.Name, @"\A[A-Za-z0-9_][A-Za-z0-9_.-]*\.dll\z", RegexOptions.IgnoreCase) ||
                !Version.TryParse(reference.Version, out var version) || version.Build < 0 || version.Revision < 0 ||
                version.Major > 65535 || version.Minor > 65535 || version.Build > 65535 || version.Revision > 65535)
                throw new InvalidDataException("Assembly references require a DLL basename and four-part assembly version.");
        }
    }

    /// <summary>Reads assembly versions from explicit host/runtime DLL paths; native DLLs are skipped.</summary>
    public static IReadOnlyDictionary<string, Version> ReadAssemblyVersions(IEnumerable<string> assemblyPaths)
    {
        ArgumentNullException.ThrowIfNull(assemblyPaths);
        var result = new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in assemblyPaths)
        {
            try
            {
                var assembly = AssemblyName.GetAssemblyName(path);
                string name = assembly.Name + ".dll";
                if (result.TryGetValue(name, out var previous) && previous != assembly.Version)
                    throw new InvalidDataException($"Conflicting installed assembly versions for '{name}'.");
                result[name] = assembly.Version!;
            }
            catch (BadImageFormatException) { }
        }
        return result;
    }
}
