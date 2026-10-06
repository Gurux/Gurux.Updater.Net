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

using Gurux.Updater.Model;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gurux.Updater.Services;

/// <summary>
/// Reads update catalogs and returns releases for catalog products.
/// </summary>
public sealed class GXCatalogUpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes the catalog update service with the supplied HTTP client.
    /// </summary>
    public GXCatalogUpdateService(HttpClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Downloads and validates an update catalog.
    /// </summary>
    /// <param name="catalogUri">The URI of the catalog document.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The validated update catalog.</returns>
    public async Task<GXUpdateCatalog> GetCatalogAsync(
        Uri catalogUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogUri);
        if (!catalogUri.IsAbsoluteUri)
        {
            throw new ArgumentException("Catalog URI must be absolute.", nameof(catalogUri));
        }

        using HttpResponseMessage response = await _client.GetAsync(catalogUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        GXUpdateCatalog catalog = await JsonSerializer.DeserializeAsync<GXUpdateCatalog>(
            stream,
            JsonOptions,
            cancellationToken)
            ?? throw new InvalidDataException("Catalog response was empty.");

        ValidateCatalog(catalog);
        return catalog;
    }

    /// <summary>
    /// Gets releases for the specified catalog product.
    /// </summary>
    /// <param name="catalog">The catalog to query.</param>
    /// <param name="productId">The product identifier to query.</param>
    /// <param name="count">The maximum number of releases to return after filtering.</param>
    /// <param name="includePrereleases">True to include prereleases; false to return stable releases only.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The newest published releases ordered from newest to oldest.</returns>
    public Task<IReadOnlyList<GXUpdateRelease>> GetReleasesAsync(
        GXUpdateCatalog catalog,
        string productId,
        int count = 10,
        bool includePrereleases = false,
        CancellationToken cancellationToken = default)
    {
        return GetReleasesAsync(
            catalog,
            productId,
            null,
            count,
            includePrereleases,
            cancellationToken);
    }

    /// <summary>
    /// Gets releases for the specified catalog product and selects an asset using the supplied pattern.
    /// </summary>
    /// <param name="catalog">The catalog to query.</param>
    /// <param name="productId">The product identifier to query.</param>
    /// <param name="assetPattern">The optional asset selection pattern.</param>
    /// <param name="count">The maximum number of releases to return after filtering.</param>
    /// <param name="includePrereleases">True to include prereleases; false to return stable releases only.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The newest published releases ordered from newest to oldest.</returns>
    public Task<IReadOnlyList<GXUpdateRelease>> GetReleasesAsync(
        GXUpdateCatalog catalog,
        string productId,
        string? assetPattern,
        int count = 10,
        bool includePrereleases = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (string.IsNullOrWhiteSpace(productId))
        {
            throw new ArgumentException("Product ID is required.", nameof(productId));
        }
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        cancellationToken.ThrowIfCancellationRequested();
        ValidateCatalog(catalog);

        GXUpdateCatalogItem item = catalog.Items.FirstOrDefault(x => string.Equals(x.Id, productId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Catalog product '{productId}' was not found.");

        GXUpdateRelease[] releases = item.Releases
            .Where(x => includePrereleases || !x.IsPrerelease)
            .OrderByDescending(x => x.PublishedAt ?? DateTimeOffset.MinValue)
            .Take(count)
            .Select(x => CloneRelease(x, assetPattern))
            .ToArray();

        return Task.FromResult<IReadOnlyList<GXUpdateRelease>>(releases);
    }

    private static GXUpdateRelease CloneRelease(GXUpdateRelease release, string? assetPattern)
    {
        List<GXUpdateAsset> assets = release.Assets
            .Select(x => new GXUpdateAsset
            {
                Name = x.Name,
                DownloadUrl = x.DownloadUrl,
                Size = x.Size,
                Digest = x.Digest
            })
            .ToList();

        return new GXUpdateRelease
        {
            Version = release.Version,
            TagName = release.TagName,
            IsPrerelease = release.IsPrerelease,
            PublishedAt = release.PublishedAt,
            ReleaseNotes = release.ReleaseNotes,
            ReleaseUrl = release.ReleaseUrl,
            Assets = assets,
            Asset = GXUpdateAssetSelector.SelectAsset(assets, assetPattern)
        };
    }

    private static void ValidateCatalog(GXUpdateCatalog catalog)
    {
        if (catalog.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported catalog schema version '{catalog.SchemaVersion}'.");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (GXUpdateCatalogItem item in catalog.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                throw new InvalidDataException("Catalog item ID is required.");
            }
            if (!ids.Add(item.Id))
            {
                throw new InvalidDataException($"Duplicate catalog item ID '{item.Id}'.");
            }

            foreach (GXUpdateRelease release in item.Releases)
            {
                if (string.IsNullOrWhiteSpace(release.Version))
                {
                    throw new InvalidDataException($"Catalog release version is required for '{item.Id}'.");
                }
                if (release.PublishedAt is null)
                {
                    throw new InvalidDataException($"Catalog release publishedAt is required for '{item.Id}' version '{release.Version}'.");
                }
                if (!string.IsNullOrWhiteSpace(release.ReleaseUrl) &&
                    !Uri.TryCreate(release.ReleaseUrl, UriKind.Absolute, out _))
                {
                    throw new InvalidDataException($"Catalog release URL is invalid for '{item.Id}' version '{release.Version}'.");
                }

                foreach (GXUpdateAsset asset in release.Assets)
                {
                    if (string.IsNullOrWhiteSpace(asset.Name))
                    {
                        throw new InvalidDataException($"Catalog asset name is required for '{item.Id}' version '{release.Version}'.");
                    }
                    if (!Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out Uri? uri) ||
                        !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"Catalog asset downloadUrl must be an absolute HTTPS URI for '{item.Id}' version '{release.Version}'.");
                    }
                    if (asset.Size < 0)
                    {
                        throw new InvalidDataException($"Catalog asset size must be non-negative for '{item.Id}' version '{release.Version}'.");
                    }
                }
            }
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}