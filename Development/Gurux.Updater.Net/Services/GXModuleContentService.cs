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

using System.Text.Json;
using Gurux.Updater.Model;
using NuGet.Versioning;

namespace Gurux.Updater.Services;

/// <summary>Lists and installs independently versioned module content without touching module binaries.</summary>
public sealed class GXModuleContentService(HttpClient client)
{
    readonly GXCatalogSourceReader reader = new(client, allowLoopbackHttp: true);

    static void Kind(string kind)
    {
        if (kind is not ("localization" or "resources"))
            throw new ArgumentException("Module content kind must be localization or resources.");
    }

    /// <summary>Lists compatible stable module releases from the corresponding localization/resource index.</summary>
    public async Task<IReadOnlyList<GXContentManifest>> ListAsync(string index, string owner, string kind, string ownerVersion,
        CancellationToken token = default)
    {
        Kind(kind);
        GXLocalizationJson.ValidateOwner(owner, "module");
        var version = GXLocalizationJson.Version(ownerVersion);
        IEnumerable<GXContentManifest> manifests;
        if (kind == "resources")
        {
            var catalog = GXLocalizationJson.Read<GXResourceCatalog>(await reader.ReadAsync(index, token), index);
            if (catalog.SchemaVersion != 1 || catalog.Items == null)
                throw new InvalidDataException("Invalid resource index.");
            foreach (var manifest in catalog.Items)
                GXContentReleaseService.ValidateManifest(manifest);
            manifests = catalog.Items.Where(x => x.ModulePackage);
        }
        else
        {
            var catalog = await new GXLocalizationService(client, allowLoopbackHttp: true).GetCatalogAsync(index, token);
            manifests = catalog.Items.SelectMany(item => item.Releases.Where(x => x.ModulePackage).Select(release => new GXContentManifest
            {
                ModulePackage = true,
                Kind = "localization",
                Owner = item.Owner,
                OwnerType = item.OwnerType,
                Unit = "all",
                Version = release.Version,
                OwnerVersionRange = release.OwnerVersionRange,
                Sha256 = release.Sha256,
                Size = release.Size,
                ContentSha256 = release.ContentSha256 ?? ""
            }));
        }
        var all = manifests.ToList();
        foreach (var manifest in all)
        {
            GXContentReleaseService.ValidateManifest(manifest);
            if (manifest.Kind != kind)
                throw new InvalidDataException("Index contains the wrong content kind.");
        }
        if (all.GroupBy(x => x.Tag, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
            throw new InvalidDataException("Duplicate module content release.");
        return all.Where(x => x.Owner == owner && x.OwnerType == "module" && !GXLocalizationJson.Version(x.Version).IsPrerelease &&
            VersionRange.Parse(x.OwnerVersionRange!).Satisfies(version)).OrderByDescending(x => GXLocalizationJson.Version(x.Version), VersionComparer.VersionRelease).ToList();
    }

    static string ManagedRoot(string install)
    {
        string root = Path.GetFullPath(install);
        return GXLocalizationService.ManagedPath(Path.GetPathRoot(root)!, root);
    }
    static string ContentDirectory(string root, string kind) => GXLocalizationService.ManagedPath(root, kind == "localization" ? "localized" : "asset");
    static string StatePath(string root, string kind) => GXLocalizationService.ManagedPath(ContentDirectory(root, kind), ".gurux-release.json");

    static GXContentManifest? Installed(string root, string owner, string kind)
    {
        string state = StatePath(root, kind);
        if (!File.Exists(state))
            return null;
        var manifest = GXContentReleaseService.ReadManifest(File.ReadAllBytes(state));
        if (!manifest.ModulePackage || manifest.Owner != owner || manifest.Kind != kind)
            throw new InvalidDataException("Installed resource identity differs from requested module/type.");
        return manifest;
    }

    /// <summary>Checks for a compatible newer content version without modifying the installation.</summary>
    public async Task<bool> CheckAsync(string index, string owner, string kind, string ownerVersion, string install,
        CancellationToken token = default)
    {
        var latest = (await ListAsync(index, owner, kind, ownerVersion, token)).FirstOrDefault();
        if (latest == null)
            return false;
        var previous = Installed(ManagedRoot(install), owner, kind);
        return previous == null || VersionComparer.VersionRelease.Compare(GXLocalizationJson.Version(latest.Version), GXLocalizationJson.Version(previous.Version)) > 0;
    }

    /// <summary>Installs the latest compatible complete content snapshot. Replaces only localized/ or asset/; never binaries.</summary>
    public async Task<bool> UpdateAsync(string index, string owner, string kind, string ownerVersion, string install,
        CancellationToken token = default)
    {
        var latest = (await ListAsync(index, owner, kind, ownerVersion, token)).FirstOrDefault()
            ?? throw new InvalidOperationException($"No compatible {kind} for {owner} module {ownerVersion}.");
        string root = ManagedRoot(install);
        var previous = Installed(root, owner, kind);
        if (previous != null && VersionComparer.VersionRelease.Compare(GXLocalizationJson.Version(latest.Version), GXLocalizationJson.Version(previous.Version)) <= 0)
        {
            if (latest.Version == previous.Version && latest.Sha256 != previous.Sha256)
                throw new InvalidDataException("Published content changed without a new version.");
            return false;
        }
        string reference = latest.Path;
        if (kind == "localization")
        {
            var catalog = await new GXLocalizationService(client, allowLoopbackHttp: true).GetCatalogAsync(index, token);
            reference = catalog.Items.Single(x => x.Owner == owner).Releases.Single(x => x.ModulePackage && x.Version == latest.Version).Path;
        }
        byte[] bytes = await reader.ReadAsync(reader.ResolveReference(index, reference), token);
        GXContentReleaseService.ValidatePackage(latest, bytes);
        // Fully download, verify compatibility, hashes and safe paths before the first write.
        var assets = kind == "resources" ? GXContentReleaseService.ResourceFiles(latest, bytes) : null;
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(root);
        string lockPath = GXLocalizationService.ManagedPath(root, ".gurux-content.lock");
        await using var held = await GXLocalizationPublisher.LockAsync(lockPath, token);
        // Another updater may have completed while this one was downloading.
        previous = Installed(root, owner, kind);
        if (previous != null && VersionComparer.VersionRelease.Compare(GXLocalizationJson.Version(latest.Version), GXLocalizationJson.Version(previous.Version)) <= 0)
            return false;
        string target = ContentDirectory(root, kind);
        string staging = GXLocalizationService.ManagedPath(root, ".gurux-staging-" + Guid.NewGuid().ToString("N"));
        string backup = GXLocalizationService.ManagedPath(root, ".gurux-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        bool movedOld = false, installed = false;
        try
        {
            if (assets != null)
                foreach (var asset in assets)
                {
                    string path = GXLocalizationService.ManagedPath(staging, asset.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await File.WriteAllBytesAsync(path, asset.Value, token);
                }
            else
                await File.WriteAllBytesAsync(GXLocalizationService.ManagedPath(staging, owner + ".json"), bytes, token);
            await File.WriteAllBytesAsync(GXLocalizationService.ManagedPath(staging, ".gurux-release.json"),
                JsonSerializer.SerializeToUtf8Bytes(latest, GXLocalizationJson.Options), token);
            token.ThrowIfCancellationRequested();
            _ = ContentDirectory(root, kind);
            if (Directory.Exists(target))
            { Directory.Move(target, backup); movedOld = true; }
            try
            { Directory.Move(staging, target); installed = true; }
            catch
            {
                if (movedOld)
                    Directory.Move(backup, target);
                throw;
            }
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
            if (installed && Directory.Exists(backup))
                Directory.Delete(backup, true);
        }
        return true;
    }
}
