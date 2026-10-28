//
// --------------------------------------------------------------------------
//  Gurux Ltd
// 
//
//
// Filename:        $HeadURL$
//
// Version:         $Revision$,
//                  $Date$
//                  $Author$
//
// Copyright (c) Gurux Ltd
//
//---------------------------------------------------------------------------
//
//  DESCRIPTION
//
// This file is a part of Gurux Device Framework.
//
// Gurux Device Framework is Open Source software; you can redistribute it
// and/or modify it under the terms of the GNU General Public License 
// as published by the Free Software Foundation; version 2 of the License.
// Gurux Device Framework is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of 
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. 
// See the GNU General Public License for more details.
//
// This code is licensed under the GNU General Public License v2. 
// Full text may be retrieved at http://www.gnu.org/licenses/gpl-2.0.txt
//---------------------------------------------------------------------------

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
    private sealed record AssemblyInfo(string Path, string Name, string? Version, string AssemblyVersion, bool EntryPoint, List<(string? Id, string? Name)> Modules, List<GXAssemblyReference> References);

    /// <summary>Infers the main product, module ID and version from .NET metadata inside a ZIP.</summary>
    /// <remarks>Computed or ambiguous module IDs are returned empty so callers can supply --product.</remarks>
    public static GXCatalogPackageInfo Read(string packagePath)
        => Read(packagePath, null, null, null);

    /// <summary>Reads a ZIP using explicit identity overrides, then validates its declared compatibility.</summary>
    public static GXCatalogPackageInfo Read(string packagePath, string? product, CatalogProductType? productType, string? productVersion)
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
                    (pe.PEHeaders.CorHeader?.EntryPointTokenOrRelativeVirtualAddress ?? 0) != 0, modules,
                    metadata.AssemblyReferences.Select(handle =>
                    {
                        var reference = metadata.GetAssemblyReference(handle);
                        return new GXAssemblyReference { Name = metadata.GetString(reference.Name) + ".dll", Version = reference.Version.ToString() };
                    }).ToList()));
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
                return ApplyOverrides(zip, new GXCatalogPackageInfo { Type = CatalogProductType.Module, Version = version, IsPrerelease = version.Contains('-'), References = MergeReferences(mains, assemblies) }, product, productType, productVersion);
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
            if (product is not null && productType is not null && productVersion is not null)
                return ApplyOverrides(zip, new GXCatalogPackageInfo { Name = product }, product, productType, productVersion);
            throw new InvalidDataException("ZIP does not identify one deployable .NET application or module. Include its main .deps.json or .runtimeconfig.json metadata.");
        }

        AssemblyInfo main = mains[0];
        bool module = !application && main.Modules.Count != 0;
        string id = module ? main.Modules.Count == 1 ? main.Modules[0].Id ?? string.Empty : string.Empty : main.Name;
        string? name = module && main.Modules.Count == 1 ? main.Modules[0].Name : main.Name;
        string versionValue = main.Version ?? DepsVersion(zip, main) ?? main.AssemblyVersion;
        return ApplyOverrides(zip, new GXCatalogPackageInfo
        {
            References = MergeReferences([main], assemblies),
            Id = id,
            Name = name,
            Type = module ? CatalogProductType.Module : CatalogProductType.Application,
            Version = versionValue,
            IsPrerelease = versionValue.Contains('-')
        }, product, productType, productVersion);
    }

    private static GXCatalogPackageInfo ApplyOverrides(ZipArchive zip, GXCatalogPackageInfo package,
        string? product, CatalogProductType? productType, string? productVersion)
    {
        if (product is not null) package.Id = product;
        if (productType is not null) package.Type = productType.Value;
        if (productVersion is not null) package.Version = productVersion;
        package.IsPrerelease = package.Version.Split('+')[0].Contains('-');
        return ReadDeclaredCompatibility(zip, package);
    }

    private static GXCatalogPackageInfo ReadDeclaredCompatibility(ZipArchive zip, GXCatalogPackageInfo package)
    {
        var entries = zip.Entries.Where(x => x.FullName == "module.compatibility.json").ToArray();
        if (entries.Length == 0) return package;
        if (entries.Length != 1 || entries[0].Length > 64 * 1024)
            throw new InvalidDataException("Invalid module compatibility sidecar.");
        using var input = entries[0].Open();
        using var metadata = JsonDocument.Parse(input);
        var root = metadata.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("id").GetString() != package.Id ||
            root.GetProperty("version").GetString()?.Split('+')[0] != package.Version.Split('+')[0])
            throw new InvalidDataException("Module compatibility sidecar identity/version mismatch.");
        if (root.TryGetProperty("amiVersionRange", out var range))
        {
            package.AmiVersionRange = range.GetString() ?? throw new InvalidDataException("Invalid amiVersionRange.");
            GXCatalogMetadata.ValidateAmiVersionRange(package.AmiVersionRange);
        }
        return package;
    }

    private static List<GXAssemblyReference> MergeReferences(IEnumerable<AssemblyInfo> mains, List<AssemblyInfo> assemblies)
    {
        var pending = new Queue<AssemblyInfo>(mains);
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        List<GXAssemblyReference> references = [];
        while (pending.TryDequeue(out var current))
        {
            if (!visited.Add(current.Path)) continue;
            references.AddRange(current.References);
            foreach (var reference in current.References)
            {
                foreach (var bundled in assemblies.Where(x => string.Equals(x.Name + ".dll", reference.Name, StringComparison.OrdinalIgnoreCase)
                    && x.AssemblyVersion == reference.Version))
                    pending.Enqueue(bundled);
            }
        }
        return references.DistinctBy(x => (x.Name, x.Version))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Version).ToList();
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
