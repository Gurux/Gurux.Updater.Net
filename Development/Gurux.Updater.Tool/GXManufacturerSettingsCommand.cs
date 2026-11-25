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

using System.Text.Json;
using System.Text.Json.Serialization;
using Gurux.Updater.Model;
using Gurux.Updater.Services;
namespace Gurux.Updater.Tool;

/// <summary>Lists the public DeviceProfiles index without downloading individual settings.</summary>
public static class GXManufacturerSettingsCommand
{
    private sealed record Setting(string Manufacturer, string Model,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Interface,
        string Version, string Id, string Name, string Location,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ManufacturerId,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GXDeviceProfileImage? ManufacturerImage,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GXDeviceProfileImage? ModelImage,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<GXCommunicationProfile>? Profiles);

    /// <summary>Fetches the manufacturer index, then writes a table or JSON array of available settings.</summary>
    public static async Task RunAsync(HttpClient client, GXUpdaterOptions options, TextWriter output, CancellationToken cancellationToken = default)
    {
        using var document = await LoadIndexAsync(client, options.CatalogUrl, cancellationToken);
        var index = document.RootElement.Deserialize<GXDeviceProfileIndex>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Manufacturer index is empty.");
        // Use the same schema, identity, connection and integrity validation as library callers.
        _ = new GXDeviceProfileUpdateService(client).GetProfiles(index);

        var settings = new List<Setting>();
        foreach (var manufacturer in index.Manufacturers)
        {
            if (options.ManufacturerId is not null &&
                !string.Equals(manufacturer.Id, options.ManufacturerId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            foreach (var model in manufacturer.Models)
            {
                foreach (var (connection, version, setting) in EnumerateSettings(model, index.SchemaVersion))
                {
                    settings.Add(new(manufacturer.Name, model.Name, connection, version,
                        setting.Id, setting.Name, setting.Location, manufacturer.Id, manufacturer.Image, model.Image,
                        index.SchemaVersion == 3 ? setting.Profiles : null));
                }
            }
        }

        if (options.Json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(settings, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            return;
        }
        await output.WriteLineAsync("Manufacturer | Model | Interface | Version | Setting");
        foreach (var item in settings)
        {
            string manufacturer = string.IsNullOrEmpty(item.ManufacturerId)
                ? item.Manufacturer : $"{item.Manufacturer} ({item.ManufacturerId})";
            string connection = item.Interface ?? string.Join(", ", item.Profiles?.Select(p => p.InterfaceType) ?? []);
            await output.WriteLineAsync($"{manufacturer} | {item.Model} | {connection} | {item.Version} | {item.Name}");
        }

        await output.WriteLineAsync($"{settings.Count} settings.");
    }

    private static IEnumerable<(string? Interface, string Version, GXDeviceProfileEntry Entry)> EnumerateSettings(
        GXDeviceProfileModel model, int schemaVersion)
    {
        if (schemaVersion == 2)
        {
            foreach (var connection in model.Interfaces)
            {
                foreach (var version in connection.Versions)
                {
                    foreach (var entry in version.Settings)
                    {
                        yield return (connection.Name, version.Name, entry);
                    }
                }
            }
            yield break;
        }
        if (schemaVersion == 1)
        {
            foreach (var entry in model.Settings)
            {
                yield return (null, string.Empty, entry);
            }
        }
        foreach (var version in model.Versions)
        {
            foreach (var entry in version.Settings)
            {
                yield return (null, version.Name, entry);
            }
        }
    }

    private static async Task<JsonDocument> LoadIndexAsync(HttpClient client, Uri uri, CancellationToken cancellationToken)
    {
        var document = await LoadDocumentAsync(client, uri, cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !(document.RootElement.TryGetProperty("items", out _) || document.RootElement.TryGetProperty("Items", out _)))
        {
            return document;
        }

        using (document)
        {
            var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            var catalog = document.RootElement.Deserialize<GXUpdateCatalog>(jsonOptions)!;
            if (!catalog.Items.Any(item => item.Id == "Gurux.DLMS.DeviceProfiles"))
            {
                throw new KeyNotFoundException($"Catalog '{uri}' does not contain Gurux.DLMS.DeviceProfiles. Specify a manufacturer index using --catalog-url <url>.");
            }
            var releases = await new GXCatalogUpdateService(client).GetReleasesAsync(
                catalog, "Gurux.DLMS.DeviceProfiles", "manufacturers.json", count: 1, cancellationToken: cancellationToken);
            var asset = releases.FirstOrDefault()?.Asset
                ?? throw new InvalidDataException($"Catalog '{uri}' has no stable DeviceProfiles release with a manufacturers.json asset.");
            return await LoadDocumentAsync(client, new Uri(asset.DownloadUrl), cancellationToken);
        }
    }

    private static async Task<JsonDocument> LoadDocumentAsync(HttpClient client, Uri uri, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Unable to load manufacturer index '{uri}': {(int)response.StatusCode} ({response.ReasonPhrase}). Specify an available manufacturer index using --catalog-url <url>.", null, response.StatusCode);
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}
