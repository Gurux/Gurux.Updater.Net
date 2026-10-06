using System.Text.Json;
namespace Gurux.Updater.Tool;

/// <summary>Lists the public DeviceProfiles index without downloading individual settings.</summary>
public static class GXManufacturerSettingsCommand
{
    private sealed record Setting(string Manufacturer, string Model, string Interface, string Version, string Id, string Name, string Location);

    /// <summary>Fetches the manufacturer index, then writes a table or JSON array of available settings.</summary>
    public static async Task RunAsync(HttpClient client, GXUpdaterOptions options, TextWriter output, CancellationToken cancellationToken = default)
    {
        var uri = options.CatalogUrl;
        using var response = await client.GetAsync(uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Unable to load manufacturer index '{uri}': {(int)response.StatusCode} ({response.ReasonPhrase}). Specify an available manufacturer index using --catalog-url <https-url>.", null, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.GetProperty("SchemaVersion").GetInt32() != 2)
            throw new InvalidDataException("Unsupported manufacturer index schema. Expected SchemaVersion 2.");
        var settings = new List<Setting>();
        foreach (var manufacturer in root.GetProperty("Manufacturers").EnumerateArray())
        foreach (var model in manufacturer.GetProperty("Models").EnumerateArray())
        foreach (var connection in model.GetProperty("Interfaces").EnumerateArray())
        foreach (var version in connection.GetProperty("Versions").EnumerateArray())
        foreach (var setting in version.GetProperty("Settings").EnumerateArray())
        {
            settings.Add(new(manufacturer.GetProperty("Name").GetString()!, model.GetProperty("Name").GetString()!,
                connection.GetProperty("Name").GetString()!, version.GetProperty("Name").GetString()!,
                setting.GetProperty("Id").GetString()!, setting.GetProperty("Name").GetString()!, setting.GetProperty("Location").GetString()!));
        }
        if (options.Json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(settings, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            return;
        }
        await output.WriteLineAsync("Manufacturer | Model | Interface | Version | Setting");
        foreach (var item in settings)
            await output.WriteLineAsync($"{item.Manufacturer} | {item.Model} | {item.Interface} | {item.Version} | {item.Name}");
        await output.WriteLineAsync($"{settings.Count} settings.");
    }
}
