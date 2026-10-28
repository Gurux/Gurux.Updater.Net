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
using Gurux.Updater.Services;

namespace Gurux.Updater.Tool;

/// <summary>Explicit standalone script publication, download, update and local change rejection.</summary>
public static class GXScriptCommand
{
    /// <summary>Script command usage.</summary>
    public const string HelpText = """
        gurux-updater script list [--catalog-url <url>] [--prerelease] [--json]
        gurux-updater script publish --manifest <manifest.json> --file <source.cs|source.csx> [--catalog-url <url>]
        gurux-updater script download --id <id> [--version <version>] --output <file> --ami-version <version> [--catalog-url <url>] [--prerelease]
        gurux-updater script validate-manifest --manifest <manifest.json>
        gurux-updater script status --file <installed-script>
        gurux-updater script update --file <installed-script> --ami-version <version> [--discard-local-changes] [--prerelease]
        gurux-updater script reject --file <installed-script>
        --catalog-url accepts the shared catalog or scripts/script-index.json; default http://localhost:8000/scripts/script-index.json.
        Scripts are never executed. Update and reject retain a uniquely named .bak copy.
        """;

    /// <summary>Runs a script command using an injected client and output.</summary>
    public static async Task RunAsync(HttpClient client, string[] args, TextWriter output, CancellationToken token = default)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"])
        {
            await output.WriteLineAsync(HelpText);
            return;
        }
        var command = args[0];
        string[] permitted = command switch
        {
            "list" => ["--catalog-url", "--prerelease", "--json"],
            "publish" => ["--catalog-url", "--manifest", "--file", "--json"],
            "download" => ["--catalog-url", "--id", "--version", "--output", "--ami-version", "--prerelease", "--json"],
            "validate-manifest" => ["--manifest", "--json"],
            "status" or "reject" => ["--file", "--json"],
            "update" => ["--file", "--ami-version", "--discard-local-changes", "--prerelease", "--json"],
            _ => throw new ArgumentException($"Unknown script command '{command}'.\n{HelpText}")
        };
        Dictionary<string, string?> options = new(StringComparer.Ordinal);
        for (int pos = 1; pos < args.Length; pos++)
        {
            string name = args[pos];
            if (!permitted.Contains(name) || options.ContainsKey(name)) throw new ArgumentException($"Unknown or duplicate option '{name}'.");
            string? value = null;
            if (name is not ("--json" or "--prerelease" or "--discard-local-changes"))
            {
                if (++pos == args.Length || args[pos].StartsWith("--")) throw new ArgumentException($"Value required for {name}.");
                value = args[pos];
            }
            options.Add(name, value);
        }
        string Required(string name) => options.GetValueOrDefault(name) is string value && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"{name} is required.");
        bool Has(string name) => options.ContainsKey(name);
        var service = new GXScriptService(client);
        object result;
        if (command == "validate-manifest")
        {
            await service.ReadManifestAsync(Required("--manifest"), token);
            result = new { valid = true };
        }
        else if (command == "status")
        {
            var path = Required("--file");
            var state = await service.GetInstalledAsync(path, token);
            result = new { state.Manifest.Id, state.Manifest.Version, modified = await service.IsModifiedAsync(path, token) };
        }
        else if (command == "reject")
        {
            result = new { backup = await service.RejectChangesAsync(Required("--file"), token) };
        }
        else if (command == "update")
        {
            result = new { updated = await service.UpdateAsync(Required("--file"), Required("--ami-version"), Has("--discard-local-changes"), Has("--prerelease"), token) };
        }
        else
        {
            string source = options.GetValueOrDefault("--catalog-url") ?? "http://localhost:8000/scripts/script-index.json";
            if (!Uri.TryCreate(source, UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https"))
                throw new ArgumentException("--catalog-url requires an HTTP(S) URL.");
            if (address.AbsolutePath == "/") address = new Uri(address, "catalog.json");
            if (!address.AbsolutePath.EndsWith("/script-index.json", StringComparison.OrdinalIgnoreCase))
            {
                var catalogService = new GXCatalogUpdateService(client);
                var catalog = await catalogService.GetCatalogAsync(address, token);
                var releases = await catalogService.GetReleasesAsync(catalog, "Gurux.AMI.Scripts", "script-index.json", count: 1, cancellationToken: token);
                address = new Uri(releases.FirstOrDefault()?.Asset?.DownloadUrl ?? throw new InvalidDataException("Catalog has no Gurux.AMI.Scripts script-index.json asset."));
            }
            source = address.AbsoluteUri;
            if (command == "publish")
            {
                await service.PublishAsync(address, Required("--manifest"), Required("--file"), token);
                result = new { published = true };
            }
            else
            {
                var catalog = await service.GetCatalogAsync(source, token);
                if (command == "list")
                    result = catalog.Items.GroupBy(m => m.Id).Select(g =>
                    {
                        try { return service.Select(catalog, g.Key, includePrereleases: Has("--prerelease")); }
                        catch (InvalidDataException) { return null; }
                    }).Where(m => m != null).ToArray();
                else
                {
                    var release = service.Select(catalog, Required("--id"), options.GetValueOrDefault("--version"), Has("--prerelease"));
                    string destination = Required("--output");
                    await service.InstallAsync(source, release, destination, Required("--ami-version"), token);
                    result = new { release.Id, release.Version, path = Path.GetFullPath(destination) };
                }
            }
        }
        await output.WriteLineAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }
}
