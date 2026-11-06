// --------------------------------------------------------------------------
// Gurux Ltd
//
// Copyright (c) Gurux Ltd.
//
// This file is part of Gurux Device Framework.
//
// Commit: $Format:%H$
// Date:   $Format:%cI$
//
// This software is available under either of the following licenses:
//
// 1. GNU General Public License, version 2 only (GPL-2.0-only).
//    See the LICENSE file included with this distribution.
//
// 2. A commercial license granted by Gurux Ltd under a separate written
//    agreement. Use under this option requires a valid commercial license
//    and is governed by the terms of that agreement.
//
// If you have not obtained a commercial license from Gurux Ltd,
// use of this software is governed by GPL-2.0-only.
//
// Commercial licensing: https://www.gurux.fi/
//
// SPDX-License-Identifier: GPL-2.0-only OR LicenseRef-Gurux-Commercial
// --------------------------------------------------------------------------

using Gurux.Updater.Model;
using Gurux.Updater.Enums;
using Gurux.Updater.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gurux.Updater.Tool;

/// <summary>Lists update catalog products and available releases without installing or downloading packages.</summary>
public static class GXCatalogCommand
{
    /// <summary>Checks an installed catalog product without downloading or installing it.</summary>
    public static async Task<int> CheckAsync(HttpClient client, GXUpdaterOptions options, TextWriter output,
        CancellationToken cancellationToken = default)
    {
        var info = await new GXCatalogUpdateService(client).CheckAsync(options.CatalogUrl.AbsoluteUri,
            options.Product!, new GXUpdateTarget
            {
                Name = options.Product,
                CurrentVersion = options.CurrentVersion,
                AssetPattern = options.AssetPattern,
                AmiVersion = options.AmiVersion
            }, options.IncludePrereleases, cancellationToken);
        if (options.Json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(info, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        }
        else
        {
            await output.WriteLineAsync(GXUpdateText.Status(info));
            if (info.UpdateAvailable)
            {
                await output.WriteLineAsync($"{info.CurrentVersion} -> {info.LatestVersion}");
            }
            if (info.Error is not null)
            {
                await output.WriteLineAsync(info.Error);
            }
        }
        return info.Status == GXUpdateStatus.CheckFailed ? 1 : info.UpdateAvailable ? 10 : 0;
    }
    /// <summary>Reads the catalog and writes its selected releases as a table or JSON document.</summary>
    public static async Task RunAsync(HttpClient client, GXUpdaterOptions options, TextWriter output,
        CancellationToken cancellationToken = default)
    {
        var service = new GXCatalogUpdateService(client);
        var source = await service.GetCatalogAsync(options.CatalogUrl, cancellationToken);
        var catalog = new GXUpdateCatalog { SchemaVersion = source.SchemaVersion, GeneratedAt = source.GeneratedAt };
        IEnumerable<GXUpdateCatalogItem> items = source.Items;
        if (options.ListModules || options.ListApplications || options.ListAgents)
        {
            items = items.Where(item => options.ListModules && item.Type == CatalogProductType.Module ||
            options.ListApplications && item.Type == CatalogProductType.Application ||
            options.ListAgents && item.Type == CatalogProductType.Agent);
        }

        if (options.Product != null)
        {
            var item = items.SingleOrDefault(item => string.Equals(item.Id, options.Product, StringComparison.Ordinal))
                ?? throw new KeyNotFoundException($"Catalog product '{options.Product}' was not found in the requested product types.");
            items = [item];
        }
        items = items.Where(item => (options.MaintenanceStatus is null || string.Equals(item.MaintenanceStatus, options.MaintenanceStatus, StringComparison.OrdinalIgnoreCase)) &&
            (options.Category is null || string.Equals(item.Category, options.Category, StringComparison.OrdinalIgnoreCase)) &&
            (options.Audience is null || string.Equals(item.Audience, options.Audience, StringComparison.OrdinalIgnoreCase)));
        foreach (var item in items)
        {
            var releases = await service.GetReleasesAsync(source, item.Id, options.AssetPattern,
                options.Count, options.IncludePrereleases, cancellationToken);
            catalog.Items.Add(new GXUpdateCatalogItem
            {
                Id = item.Id,
                Type = item.Type,
                Name = item.Name,
                IconUrl = item.IconUrl,
                Description = item.Description,
                Repository = item.Repository,
                MaintenanceStatus = item.MaintenanceStatus,
                Category = item.Category,
                Audience = item.Audience,
                Releases = releases.ToList()
            });
        }
        if (options.Json)
        {
            var settings = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
            settings.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            await output.WriteLineAsync(JsonSerializer.Serialize(catalog, settings));
            return;
        }
        await output.WriteLineAsync($"Catalog: {options.CatalogUrl}");
        if (catalog.Items.Count == 0)
        {
            await output.WriteLineAsync("No products in the catalog.");
        }

        foreach (var item in catalog.Items)
        {
            await output.WriteLineAsync($"{item.Id} ({item.Type}){(string.IsNullOrWhiteSpace(item.Name) ? "" : " - " + item.Name)}");
            await output.WriteLineAsync($"  Maintenance: {item.MaintenanceStatus ?? "unknown"}; category: {item.Category ?? "unknown"}; audience: {item.Audience ?? "unknown"}");
            if (item.Releases.Count == 0)
            {
                await output.WriteLineAsync("  No releases available.");
            }

            foreach (var release in item.Releases)
            {
                if (release.AmiVersionRange is not null)
                {
                    await output.WriteLineAsync($"  Works with AMI: {release.AmiVersionRange}");
                }
                await output.WriteLineAsync($"  {release.Version}{(release.IsPrerelease ? " (prerelease)" : "")} — {GXUpdateText.Priority(release.Priority, release.IsSecurityUpdate)}{(release.IsRevoked ? " — release revoked" : release.IsUnsupported ? " — no longer supported" : "")}");
                List<GXUpdateAsset> assets = options.AssetPattern == null ? release.Assets :
                    release.Asset == null ? [] : [release.Asset];
                if (options.AssetPattern != null && assets.Count == 0)
                {
                    await output.WriteLineAsync($"    No package matches '{options.AssetPattern}'.");
                }

                foreach (var asset in assets)
                {
                    await output.WriteLineAsync($"    {asset.Name}: {asset.DownloadUrl}");
                }
            }
        }
    }
}
