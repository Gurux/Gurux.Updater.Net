using System.Text.Json;
using Gurux.Updater.Model;
using Gurux.Updater.Services;

namespace Gurux.Updater.Tool;

/// <summary>Lists, adds, updates, downloads and validates complete localization packages.</summary>
public static class GXLocalizationCommand
{
    /// <summary>Finds the HTTP localization index in the shared update catalog.</summary>
    public static async Task<Uri> ResolveIndexAsync(HttpClient client, Uri catalogUrl, CancellationToken cancellationToken = default)
    {
        var service = new GXCatalogUpdateService(client);
        var catalog = await service.GetCatalogAsync(catalogUrl, cancellationToken);
        var releases = await service.GetReleasesAsync(catalog, "Gurux.AMI.Localization", "localization-index.json",
            count: 1, includePrereleases: true, cancellationToken: cancellationToken);
        var asset = releases.FirstOrDefault()?.Asset
            ?? throw new InvalidDataException("The update catalog has no localization-index.json asset.");
        var uri = new Uri(asset.DownloadUrl);
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Localization catalogs require an HTTP or HTTPS URL.");
        return uri;
    }
    /// <summary>Lists or installs localization packages using the shared updater options.</summary>
    public static async Task RunAsync(HttpClient client, GXUpdaterOptions options, TextWriter output,
        CancellationToken cancellationToken = default, TextWriter? diagnostics = null)
    {
        var catalogService = new GXCatalogUpdateService(client);
        var catalog = await catalogService.GetCatalogAsync(options.CatalogUrl, cancellationToken);
        if (!catalog.Items.Any(item => item.Id == "Gurux.AMI.Localization"))
        {
            throw new InvalidDataException($"Catalog '{options.CatalogUrl}' does not contain Gurux.AMI.Localization. Register its localization-index.json asset in the update catalog.");
        }
        var releases = await catalogService.GetReleasesAsync(catalog, "Gurux.AMI.Localization",
            "localization-index.json", count: 1, includePrereleases: options.IncludePrereleases, cancellationToken: cancellationToken);
        var asset = releases.FirstOrDefault()?.Asset
            ?? throw new InvalidDataException($"Catalog '{options.CatalogUrl}' has no localization release with a localization-index.json asset.");
        if (options.Localization != null)
        {
            var service = new GXLocalizationService(client, allowHttp: true);
            bool updated;
            GXConsoleProgress? progress = options.Json ? null : new GXConsoleProgress(diagnostics ?? Console.Error);
            try
            {
                updated = await service.UpdatePackageAsync(asset.DownloadUrl, options.Localization,
                    options.IncludePrereleases, progress, cancellationToken);
            }
            finally
            {
                progress?.Complete();
            }
            if (options.Json)
            {
                await output.WriteLineAsync(JsonSerializer.Serialize(new { path = Path.GetFullPath(options.Localization), updated }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            }
            else
            {
                await output.WriteLineAsync(updated ? $"Localization package updated: {Path.GetFullPath(options.Localization)}" : await UpToDateMessageAsync(service, asset.DownloadUrl, options.Localization, options.IncludePrereleases, cancellationToken));
            }
            return;
        }
        List<string> args = ["list", "--catalog-url", options.CatalogUrl.AbsoluteUri];
        if (options.Product != null) args.AddRange(["--owner", options.Product]);
        if (options.IncludePrereleases) args.Add("--include-prereleases");
        if (options.Json) args.Add("--json");
        await ExecuteAsync(client, args.ToArray(), output, cancellationToken, diagnostics, asset.DownloadUrl);
    }

    private static async Task<string> UpToDateMessageAsync(GXLocalizationService service, string index, string path,
        bool prereleases, CancellationToken cancellationToken)
    {
        if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return "Up to date: all localization packages in the ZIP match or exceed catalog versions.";
        var package = await service.ReadPackageAsync(path, cancellationToken);
        var catalog = await service.GetCatalogAsync(index, cancellationToken);
        var release = service.List(catalog, owner: package.Owner, includePrereleases: prereleases).First().Releases[0];
        return $"Up to date: {package.Owner}, local {package.Version}, catalog {release.Version}.";
    }

    /// <summary>CLI usage for the localization command group.</summary>
    public const string HelpText = """
        gurux-updater publish <localization.json> [--catalog-url <http-or-https-url>] [--json]
        gurux-updater localization list [--catalog-url <http-or-https-url>]
            [--owner <id>] [--owner-type application|module] [--culture <culture>]
            [--all-versions] [--include-prereleases] [--json]
        gurux-updater localization download [--catalog-url <http-or-https-url>] --owner <id> --output <directory>
            [--owner-version <version>] [--version <package-version>] [--include-prereleases] [--json]
        gurux-updater localization validate [--catalog-url <http-or-https-url>] [--json]
        gurux-updater localization validate-package --package <local-json-file>
        Default catalog: http://localhost:8000/catalog.json. Local catalog paths are unsupported.
        """;
    /// <summary>Executes arguments following localization, with cancellation and separate progress diagnostics.</summary>
    public static async Task RunAsync(HttpClient client, string[] args, TextWriter output,
        CancellationToken cancellationToken = default, TextWriter? diagnostics = null, bool allowLoopbackHttp = false)
        => await ExecuteAsync(client, args, output, cancellationToken, diagnostics);

    private static async Task ExecuteAsync(HttpClient client, string[] args, TextWriter output,
        CancellationToken cancellationToken, TextWriter? diagnostics, string? resolvedIndex = null)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"])
        {
            await output.WriteLineAsync(HelpText);
            return;
        }
        string command = args[0];
        string[] permitted = command switch
        {
            "list" => ["--catalog-url", "--owner", "--owner-type", "--culture", "--all-versions", "--include-prereleases", "--json"],
            "download" => ["--catalog-url", "--owner", "--output", "--owner-version", "--version", "--include-prereleases", "--json"],
            "validate" => ["--catalog-url", "--json"],
            "validate-package" => ["--package"],
            _ => throw new ArgumentException($"Unknown localization command '{command}'.\n{HelpText}")
        };
        HashSet<string> flags = ["--json", "--all-versions", "--include-prereleases", "--replace"];
        Dictionary<string, string?> options = new(StringComparer.Ordinal);
        for (int pos = 1; pos < args.Length; ++pos)
        {
            string name = args[pos];
            if (name is "--help" or "-h")
            {
                await output.WriteLineAsync(HelpText);
                return;
            }
            if (!permitted.Contains(name, StringComparer.Ordinal))
            {
                throw new ArgumentException($"Unknown option '{name}' for localization {command}.");
            }
            if (options.ContainsKey(name))
            {
                throw new ArgumentException($"Duplicate option '{name}'.");
            }
            string? value = null;
            if (!flags.Contains(name))
            {
                if (++pos >= args.Length || args[pos].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"A value is required for {name}.");
                }
                value = args[pos];
            }
            options.Add(name, value);
        }
        string Required(string name) => options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException($"{name} is required for localization {command}.");
        string? Optional(string name) => options.GetValueOrDefault(name);
        bool Has(string name) => options.ContainsKey(name);
        if (command == "validate-package")
        {
            string path = Required("--package");
            if (new GXCatalogSourceReader(client).GetRemoteUri(path) != null)
                throw new ArgumentException("--package must be a local JSON file.");
            var package = await new GXLocalizationService(client).ReadPackageAsync(path, cancellationToken);
            await output.WriteLineAsync(JsonSerializer.Serialize(new { valid = true }));
            return;
        }
        string address = Optional("--catalog-url") ?? "http://localhost:8000/catalog.json";
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("--catalog-url requires an HTTP or HTTPS URL.");
        if (uri.AbsolutePath == "/") uri = new Uri(uri, "catalog.json");
        string source = resolvedIndex ?? (await ResolveIndexAsync(client, uri, cancellationToken)).AbsoluteUri;
        bool json = Has("--json");
        var settings = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        var service = new GXLocalizationService(client, allowHttp: true);
        switch (command)
        {
            case "list":
                var catalog = await service.GetCatalogAsync(source, cancellationToken);
                var selected = service.List(catalog, Optional("--owner"), Optional("--owner-type"), Optional("--culture"), Has("--all-versions"), Has("--include-prereleases"));
                if (json)
                {
                    await output.WriteLineAsync(JsonSerializer.Serialize(new GXLocalizationCatalog
                    { SchemaVersion = catalog.SchemaVersion, GeneratedAt = catalog.GeneratedAt, Items = selected.ToList() }, settings));
                }
                else
                {
                    await output.WriteLineAsync("Owner  Type  Package version  Cultures  Owner compatibility");
                    foreach (var item in selected)
                    {
                        foreach (var release in item.Releases)
                        {
                            await output.WriteLineAsync($"{item.Owner}  {item.OwnerType}  {release.Version}  {string.Join(",", release.Cultures)}  {release.OwnerVersionRange ?? "any"}");
                        }
                    }
                    if (selected.Count == 0)
                    {
                        await output.WriteLineAsync("No matching localization packages.");
                    }
                }
                break;
            case "download":
                GXConsoleProgress? progress = json ? null : new GXConsoleProgress(diagnostics ?? Console.Error);
                string path;
                try
                {
                    path = await service.DownloadAsync(source, Required("--owner"), Required("--output"), Optional("--owner-version"),
                        Optional("--version"), Has("--include-prereleases"), progress, cancellationToken);
                }
                finally
                {
                    progress?.Complete();
                }
                await output.WriteLineAsync(json ? JsonSerializer.Serialize(new { path }, settings) : $"Localization package saved: {path}");
                break;
            case "validate":
                await service.ValidateAllAsync(source, cancellationToken);
                await output.WriteLineAsync(json ? JsonSerializer.Serialize(new { valid = true }, settings) : "All localization releases, JSON packages and SHA-256 hashes are valid.");
                break;
        }
    }
}
