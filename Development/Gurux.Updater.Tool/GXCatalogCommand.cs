using Gurux.Updater.Model;
using Gurux.Updater.Enums;
using Gurux.Updater.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gurux.Updater.Tool;

/// <summary>Lists update catalog products and available releases without installing or downloading packages.</summary>
public static class GXCatalogCommand
{
    /// <summary>Reads the catalog and writes its selected releases as a table or JSON document.</summary>
    public static async Task RunAsync(HttpClient client, GXUpdaterOptions options, TextWriter output,
        CancellationToken cancellationToken = default)
    {
        var service = new GXCatalogUpdateService(client);
        var source = await service.GetCatalogAsync(options.CatalogUrl, cancellationToken);
        var catalog = new GXUpdateCatalog { SchemaVersion = source.SchemaVersion, GeneratedAt = source.GeneratedAt };
        IEnumerable<GXUpdateCatalogItem> items = source.Items;
        if (options.ListModules || options.ListApplications)
        {
            items = items.Where(item => options.ListModules && item.Type == GXCatalogProductType.Module ||
            options.ListApplications && item.Type == GXCatalogProductType.Application);
        }

        if (options.Product != null)
        {
            var item = items.SingleOrDefault(item => string.Equals(item.Id, options.Product, StringComparison.Ordinal))
                ?? throw new KeyNotFoundException($"Catalog product '{options.Product}' was not found in the requested product types.");
            items = [item];
        }
        foreach (var item in items)
        {
            var releases = await service.GetReleasesAsync(source, item.Id, options.AssetPattern,
                options.Count, options.IncludePrereleases, cancellationToken);
            catalog.Items.Add(new GXUpdateCatalogItem
            {
                Id = item.Id,
                Type = item.Type,
                Name = item.Name,
                Description = item.Description,
                Repository = item.Repository,
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
            if (item.Releases.Count == 0)
            {
                await output.WriteLineAsync("  No releases available.");
            }

            foreach (var release in item.Releases)
            {
                await output.WriteLineAsync($"  {release.Version}{(release.IsPrerelease ? " (prerelease)" : "")}");
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
