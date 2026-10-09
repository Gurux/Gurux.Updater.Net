using System.Text.Json;
using System.Text.Json.Serialization;
using Gurux.Updater.Model;
using Gurux.Updater.Services;
namespace Gurux.Updater.Tool;

/// <summary>Lists the public DeviceProfiles index without downloading individual settings.</summary>
public static class GXManufacturerSettingsCommand
{
    private sealed record Setting(string Manufacturer, string Model, string Interface, string Version, string Id, string Name, string Location);

    /// <summary>Fetches the manufacturer index, then writes a table or JSON array of available settings.</summary>
    public static async Task RunAsync(HttpClient client, GXUpdaterOptions options, TextWriter output, CancellationToken cancellationToken = default)
    {
        using var document = await LoadIndexAsync(client, options.CatalogUrl, cancellationToken);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("SchemaVersion", out var schema) || !schema.TryGetInt32(out int schemaVersion) || schemaVersion != 2)
        {
            throw new InvalidDataException("Unsupported manufacturer index schema. Expected SchemaVersion 2. Specify a manufacturer index or an update catalog containing Gurux.DLMS.DeviceProfiles using --catalog-url <url>.");
        }

        var settings = new List<Setting>();
        foreach (var manufacturer in root.GetProperty("Manufacturers").EnumerateArray())
        {
            foreach (var model in manufacturer.GetProperty("Models").EnumerateArray())
            {
                foreach (var connection in model.GetProperty("Interfaces").EnumerateArray())
                {
                    foreach (var version in connection.GetProperty("Versions").EnumerateArray())
                    {
                        foreach (var setting in version.GetProperty("Settings").EnumerateArray())
                        {
                            settings.Add(new(manufacturer.GetProperty("Name").GetString()!, model.GetProperty("Name").GetString()!,
                                connection.GetProperty("Name").GetString()!, version.GetProperty("Name").GetString()!,
                                setting.GetProperty("Id").GetString()!, setting.GetProperty("Name").GetString()!, setting.GetProperty("Location").GetString()!));
                        }
                    }
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
            await output.WriteLineAsync($"{item.Manufacturer} | {item.Model} | {item.Interface} | {item.Version} | {item.Name}");
        }

        await output.WriteLineAsync($"{settings.Count} settings.");
    }

    private static async Task<JsonDocument> LoadIndexAsync(HttpClient client, Uri uri, CancellationToken cancellationToken)
    {
        var document = await LoadDocumentAsync(client, uri, cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("schemaVersion", out _))
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
