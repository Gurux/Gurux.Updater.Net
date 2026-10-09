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
using Gurux.Updater.Enums;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
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

    /// <summary>Publishes a local deployable ZIP and returns the updated, validated catalog.</summary>
    public async Task<GXUpdateCatalog> PublishAsync(
        Uri catalogUri,
        string packagePath,
        GXCatalogPackageInfo metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogUri);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        if (!IsSupportedUri(catalogUri))
        {
            throw new ArgumentException("Catalog URI must use HTTPS or HTTP on a loopback host.", nameof(catalogUri));
        }

        if (string.IsNullOrWhiteSpace(metadata.Id))
        {
            throw new ArgumentException("Product ID could not be inferred. Supply --product with the module's declared ID.", nameof(metadata));
        }

        if (string.IsNullOrWhiteSpace(metadata.Version))
        {
            throw new ArgumentException("Package version is required. Supply --version.", nameof(metadata));
        }

        if (metadata.Type is not (GXCatalogProductType.Module or GXCatalogProductType.Application))
        {
            throw new ArgumentException("Only module and application ZIPs can be published.", nameof(metadata));
        }

        if (!string.Equals(Path.GetExtension(packagePath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Package must be a deployable ZIP file.", nameof(packagePath));
        }

        cancellationToken.ThrowIfCancellationRequested();
        await using FileStream file = File.OpenRead(packagePath);
        using (ZipArchive zip = new(file, ZipArchiveMode.Read, leaveOpen: true))
        {
            // Read the directory before sending so invalid ZIPs never reach the server.
            _ = zip.Entries.Count;
        }
        file.Position = 0;
        List<KeyValuePair<string, string>> fields =
        [
            new("product", metadata.Id),
            new("type", metadata.Type == GXCatalogProductType.Module ? "module" : "application"),
            new("version", metadata.Version),
            new("filename", Path.GetFileName(packagePath)),
            new("prerelease", metadata.IsPrerelease ? "true" : "false")
        ];
        if (!string.IsNullOrWhiteSpace(metadata.Name))
        {
            fields.Add(new("name", metadata.Name));
        }

        UriBuilder uri = new(catalogUri);
        string query = string.Join("&", fields.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
        uri.Query = string.IsNullOrEmpty(uri.Query) ? query : uri.Query.TrimStart('?') + "&" + query;
        using HttpRequestMessage request = new(HttpMethod.Post, uri.Uri);
        request.Content = new StreamContent(file);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        request.Content.Headers.ContentLength = file.Length;
        using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string detail = await response.Content.ReadAsStringAsync(cancellationToken);
            string message = response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented
                ? "The catalog server does not support ZIP publishing. Update and restart serve_catalog.py."
                : $"Catalog publishing failed ({(int)response.StatusCode} {response.ReasonPhrase}): {detail}";
            throw new HttpRequestException(message, null, response.StatusCode);
        }
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        GXUpdateCatalog catalog = await JsonSerializer.DeserializeAsync<GXUpdateCatalog>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Catalog response was empty.");
        ValidateCatalog(catalog);
        return catalog;
    }

    /// <summary>
    /// Returns whether an absolute catalog or package URI uses HTTPS or HTTP on a loopback host.
    /// HTTP loopback addresses support local development catalogs, including their ZIP assets.
    /// </summary>
    public static bool IsSupportedUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttps ||
            uri.Scheme == Uri.UriSchemeHttp);
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

    /// <summary>Reads a local or HTTPS update catalog. Existing update assets must retain absolute HTTP(S) addresses.</summary>
    public async Task<GXUpdateCatalog> GetCatalogAsync(string catalogSource, CancellationToken cancellationToken = default)
    {
        var reader = new GXCatalogSourceReader(_client, allowLoopbackHttp: true);
        byte[] bytes = await reader.ReadAsync(catalogSource, cancellationToken);
        GXUpdateCatalog catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<GXUpdateCatalog>(bytes, JsonOptions)
                ?? throw new InvalidDataException("Catalog was empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid update catalog JSON in '{catalogSource}': {ex.Message}", ex);
        }
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
                        !IsSupportedUri(uri))
                    {
                        throw new InvalidDataException($"Catalog asset downloadUrl must be an absolute HTTPS URI or an HTTP loopback URI for '{item.Id}' version '{release.Version}'.");
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
