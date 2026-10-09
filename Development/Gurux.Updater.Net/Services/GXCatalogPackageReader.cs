using Gurux.Updater.Enums;
using Gurux.Updater.Model;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace Gurux.Updater.Services;

/// <summary>Reads deployable ZIP metadata without loading or executing package assemblies.</summary>
public static class GXCatalogPackageReader
{
    private sealed record AssemblyInfo(string Path, string Name, string? Version, string AssemblyVersion, bool EntryPoint, List<(string? Id, string? Name)> Modules);

    /// <summary>Infers the main product, module ID and version from .NET metadata inside a ZIP.</summary>
    /// <remarks>Computed or ambiguous module IDs are returned empty so callers can supply --product.</remarks>
    public static GXCatalogPackageInfo Read(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        using ZipArchive zip = ZipFile.OpenRead(packagePath);
        List<AssemblyInfo> assemblies = [];
        foreach (ZipArchiveEntry entry in zip.Entries.Where(x => x.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || x.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
        {
            using Stream input = entry.Open();
            using MemoryStream bytes = new();
            input.CopyTo(bytes);
            bytes.Position = 0;
            try
            {
                using PEReader pe = new(bytes);
                if (!pe.HasMetadata)
                {
                    continue;
                }

                MetadataReader metadata = pe.GetMetadataReader();
                if (!metadata.IsAssembly)
                {
                    continue;
                }

                AssemblyDefinition assembly = metadata.GetAssemblyDefinition();
                string? version = InformationalVersion(metadata, assembly);
                List<(string? Id, string? Name)> modules = [];
                foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
                {
                    TypeDefinition type = metadata.GetTypeDefinition(handle);
                    if ((type.Attributes & (TypeAttributes.Abstract | TypeAttributes.Interface)) != 0 || !IsModule(metadata, handle, []))
                    {
                        continue;
                    }

                    modules.Add((ConstantGetter(pe, metadata, handle, "get_Id"), ConstantGetter(pe, metadata, handle, "get_Name")));
                }
                assemblies.Add(new(entry.FullName, metadata.GetString(assembly.Name), version is null ? null : NormalizeVersion(version), assembly.Version.ToString(),
                    (pe.PEHeaders.CorHeader?.EntryPointTokenOrRelativeVirtualAddress ?? 0) != 0, modules));
            }
            catch (BadImageFormatException)
            {
                // Native dependencies are normal in application deployment ZIPs.
            }
        }

        // Runtime configuration is an application entrypoint signal; a module DLL can be its dependency.
        List<AssemblyInfo> mains = FindMains(zip, assemblies, ".runtimeconfig.json");
        bool application = mains.Count != 0;
        if (mains.Count == 0)
        {
            List<AssemblyInfo> modules = assemblies.Where(x => x.Modules.Count != 0).ToList();
            mains = FindMains(zip, modules.Count != 0 ? modules : assemblies, ".deps.json");
            if (mains.Count == 0)
            {
                mains = modules;
            }

            if (mains.Count > 1 && mains.All(x => x.Modules.Count != 0))
            {
                string[] versions = mains.Select(x => x.Version ?? DepsVersion(zip, x) ?? x.AssemblyVersion).Distinct().ToArray();
                string version = versions.Length == 1 ? versions[0] : string.Empty;
                return new GXCatalogPackageInfo { Type = GXCatalogProductType.Module, Version = version, IsPrerelease = version.Contains('-') };
            }
        }
        if (mains.Count == 0)
        {
            mains = assemblies.Where(x => x.EntryPoint).ToList();
        }

        if (mains.Count == 0 && assemblies.Count == 1)
        {
            mains = assemblies;
        }

        if (mains.Count != 1)
        {
            throw new InvalidDataException("ZIP does not identify one deployable .NET application or module. Include its main .deps.json or .runtimeconfig.json metadata.");
        }

        AssemblyInfo main = mains[0];
        bool module = !application && main.Modules.Count != 0;
        string id = module ? main.Modules.Count == 1 ? main.Modules[0].Id ?? string.Empty : string.Empty : main.Name;
        string? name = module && main.Modules.Count == 1 ? main.Modules[0].Name : main.Name;
        string versionValue = main.Version ?? DepsVersion(zip, main) ?? main.AssemblyVersion;
        return new GXCatalogPackageInfo
        {
            Id = id,
            Name = name,
            Type = module ? GXCatalogProductType.Module : GXCatalogProductType.Application,
            Version = versionValue,
            IsPrerelease = versionValue.Contains('-')
        };
    }

    private static List<AssemblyInfo> FindMains(ZipArchive zip, List<AssemblyInfo> assemblies, string suffix)
    {
        return zip.Entries.Where(x => x.FullName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => assemblies.Where(a => string.Equals(a.Path[..a.Path.LastIndexOf('.')], x.FullName[..^suffix.Length], StringComparison.OrdinalIgnoreCase)))
            .Distinct().ToList();
    }

    private static string? DepsVersion(ZipArchive zip, AssemblyInfo main)
    {
        string path = main.Path[..main.Path.LastIndexOf('.')] + ".deps.json";
        ZipArchiveEntry? entry = zip.Entries.FirstOrDefault(x => string.Equals(x.FullName, path, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        using Stream input = entry.Open();
        using JsonDocument document = JsonDocument.Parse(input);
        if (!document.RootElement.TryGetProperty("libraries", out JsonElement libraries))
        {
            return null;
        }

        foreach (JsonProperty library in libraries.EnumerateObject())
        {
            if (library.Name.StartsWith(main.Name + "/", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeVersion(library.Name[(main.Name.Length + 1)..]);
            }
        }
        return null;
    }

    private static string NormalizeVersion(string version) => version.Split('+')[0];

    private static string? InformationalVersion(MetadataReader metadata, AssemblyDefinition assembly)
    {
        foreach (CustomAttributeHandle handle in assembly.GetCustomAttributes())
        {
            CustomAttribute attribute = metadata.GetCustomAttribute(handle);
            EntityHandle declaringType = attribute.Constructor.Kind switch
            {
                HandleKind.MemberReference => metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent,
                HandleKind.MethodDefinition => metadata.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
                _ => default
            };
            if (TypeName(metadata, declaringType) != "System.Reflection.AssemblyInformationalVersionAttribute")
            {
                continue;
            }

            BlobReader value = metadata.GetBlobReader(attribute.Value);
            if (value.ReadUInt16() == 1)
            {
                return value.ReadSerializedString();
            }
        }
        return null;
    }

    private static bool IsModule(MetadataReader metadata, TypeDefinitionHandle handle, HashSet<TypeDefinitionHandle> visited)
    {
        if (!visited.Add(handle))
        {
            return false;
        }

        TypeDefinition type = metadata.GetTypeDefinition(handle);
        foreach (InterfaceImplementationHandle implementation in type.GetInterfaceImplementations())
        {
            EntityHandle implemented = metadata.GetInterfaceImplementation(implementation).Interface;
            if (TypeName(metadata, implemented) == "Gurux.DLMS.AMI.Module.IAmiModule" ||
                implemented.Kind == HandleKind.TypeDefinition && IsModule(metadata, (TypeDefinitionHandle)implemented, visited))
            {
                return true;
            }
        }
        string baseName = TypeName(metadata, type.BaseType);
        if (baseName is "Gurux.DLMS.AMI.Module.GXAmiModuleBase" or "Gurux.DLMS.AMI.Module.GXAMIModuleSettingsBase")
        {
            return true;
        }

        return !type.BaseType.IsNil && type.BaseType.Kind == HandleKind.TypeDefinition && IsModule(metadata, (TypeDefinitionHandle)type.BaseType, visited);
    }

    private static string TypeName(MetadataReader metadata, EntityHandle handle)
    {
        if (handle.IsNil)
        {
            return string.Empty;
        }

        if (handle.Kind == HandleKind.TypeReference)
        {
            TypeReference type = metadata.GetTypeReference((TypeReferenceHandle)handle);
            return metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
        }
        if (handle.Kind == HandleKind.TypeDefinition)
        {
            TypeDefinition type = metadata.GetTypeDefinition((TypeDefinitionHandle)handle);
            return metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
        }
        return string.Empty;
    }

    private static string? ConstantGetter(PEReader pe, MetadataReader metadata, TypeDefinitionHandle typeHandle, string name)
    {
        TypeDefinition type = metadata.GetTypeDefinition(typeHandle);
        foreach (MethodDefinitionHandle handle in type.GetMethods())
        {
            MethodDefinition method = metadata.GetMethodDefinition(handle);
            string methodName = metadata.GetString(method.Name);
            if (methodName != name && !methodName.EndsWith("." + name, StringComparison.Ordinal))
            {
                continue;
            }

            if (method.RelativeVirtualAddress == 0)
            {
                return null;
            }

            byte[]? il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
            if (il is null)
            {
                return null;
            }

            int start = 0;
            while (start < il.Length && il[start] == 0)
            {
                ++start; // Debug nop.
            }

            if (il.Length - start < 6 || il[start] != 0x72)
            {
                return null; // ldstr
            }

            int token = BitConverter.ToInt32(il, start + 1);
            if ((token & unchecked((int)0xff000000)) != 0x70000000)
            {
                return null;
            }

            int end = start + 5;
            while (end < il.Length && il[end] == 0)
            {
                ++end;
            }

            if (end != il.Length - 1 || il[end] != 0x2a)
            {
                return null; // ret; no calls or computed identity.
            }

            return metadata.GetUserString(MetadataTokens.UserStringHandle(token & 0x00ffffff));
        }
        return !type.BaseType.IsNil && type.BaseType.Kind == HandleKind.TypeDefinition
            ? ConstantGetter(pe, metadata, (TypeDefinitionHandle)type.BaseType, name) : null;
    }
}
