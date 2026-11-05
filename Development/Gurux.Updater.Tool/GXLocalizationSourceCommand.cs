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
using Gurux.Updater.Model;
using Gurux.Updater.Services;

namespace Gurux.Updater.Tool;

/// <summary>Source-driven localization publication and language status operations.</summary>
internal static class GXLocalizationSourceCommand
{
    internal static bool Handles(string command) => command is "validate-source" or "plan" or "publish-sources" or "publish-snapshot" or "publish-package" or "validate-store" or "status" or "update";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, PropertyNameCaseInsensitive = false };

    internal static async Task RunAsync(HttpClient client, string[] args, TextWriter output, CancellationToken token)
    {
        string command = args[0];
        string[] allowed = command switch
        {
            "publish-snapshot" => ["--snapshot", "--index", "--json"],
            "publish-package" => ["--package", "--index", "--json"],
            "validate-store" => ["--index", "--json"],
            "status" or "update" => ["--package", "--catalog-url", "--index", "--owner-version", "--include-prereleases", "--json"],
            _ => ["--source", "--sources", "--project", "--metadata-package", "--owner", "--owner-type", "--default-culture", "--owner-version-range", "--index", "--catalog-url", "--json"]
        };
        var options = new Dictionary<string, string?>();
        for (int pos = 1; pos < args.Length; ++pos)
        {
            string name = args[pos];
            if (!allowed.Contains(name) || options.ContainsKey(name))
                throw new ArgumentException($"Unknown or duplicate option '{name}' for localization {command}.");
            string? value = null;
            if (name is not ("--json" or "--include-prereleases"))
            {
                if (++pos == args.Length || args[pos].StartsWith("--"))
                    throw new ArgumentException($"A value is required for {name}.");
                value = args[pos];
            }
            options.Add(name, value);
        }
        string? Get(string name) => options.GetValueOrDefault(name);
        string Required(string name) => Get(name) ?? throw new ArgumentException($"{name} is required.");
        bool Has(string name) => options.ContainsKey(name);
        if (Has("--catalog-url") && Has("--index"))
            throw new ArgumentException("Choose --catalog-url or --index, not both.");
        string? index = Get("--index");
        if (Has("--catalog-url"))
        {
            if (!Uri.TryCreate(Required("--catalog-url"), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                throw new ArgumentException("--catalog-url requires HTTP or HTTPS.");
            index = (await GXLocalizationCommand.ResolveIndexAsync(client, uri, token)).AbsoluteUri;
        }
        var publisher = new GXLocalizationPublisher(client);
        var service = new GXLocalizationService(client, allowHttp: true);
        object result;
        if (command == "publish-package")
            result = await publisher.PublishLegacyAsync(Required("--package"), Required("--index"), token);
        else if (command == "validate-store")
        {
            await publisher.ValidateStoreAsync(Required("--index"), token);
            result = new { valid = true };
        }
        else if (command == "publish-snapshot")
        {
            // Read through the same duplicate-key validator used for packages and sources.
            var snapshot = GXLocalizationPublicationIO.ReadSnapshot(await File.ReadAllBytesAsync(Required("--snapshot"), token));
            result = await publisher.PublishSnapshotAsync(snapshot, Required("--index"), token);
        }
        else if (command is "status" or "update")
        {
            index ??= (await GXLocalizationCommand.ResolveIndexAsync(client, new("http://localhost:8000/catalog.json"), token)).AbsoluteUri;
            if (command == "status")
                result = await service.StatusAsync(index, Required("--package"), Get("--owner-version"), Has("--include-prereleases"), token);
            else
            {
                List<GXLocalizationUpdateResult> results = [];
                try
                {
                    bool updated = await service.UpdatePackageAsync(index, Required("--package"), Has("--include-prereleases"), cancellationToken: token, ownerVersion: Get("--owner-version"), results: results);
                    result = new { updated, results };
                }
                catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
                {
                    await output.WriteLineAsync(JsonSerializer.Serialize(new { updated = false, results, error = ex.Message }, Json));
                    throw;
                }
            }
        }
        else
        {
            List<GXLocalizationSource> sources;
            if (Has("--sources"))
            {
                if (Has("--source") || Has("--owner") || Has("--project") || Has("--metadata-package") || Has("--owner-type") || Has("--default-culture") || Has("--owner-version-range"))
                    throw new ArgumentException("Source overrides cannot be combined with --sources.");
                sources = GXLocalizationPublicationIO.ReadSources(Required("--sources"));
            }
            else
                sources = [new() { Directory = Required("--source"), Project = Get("--project"), MetadataPackage = Get("--metadata-package"), Owner = Get("--owner"), OwnerType = Get("--owner-type"), DefaultCulture = Get("--default-culture"), OwnerVersionRange = Get("--owner-version-range") }];
            if (sources.Count == 0)
            { await output.WriteLineAsync(JsonSerializer.Serialize(new GXLocalizationPublicationPlan(), Json)); return; }
            var snapshot = await publisher.ReadSourcesAsync(sources, token);
            if (command == "validate-source")
                result = new { valid = true, packages = snapshot.Packages.Count };
            else
            {
                if (index == null)
                    throw new ArgumentException("--index or --catalog-url is required for plan/publish-sources.");
                if (command == "plan")
                    result = await publisher.PlanSnapshotAsync(snapshot, index, token);
                else if (Uri.TryCreate(index, UriKind.Absolute, out var uri) && !uri.IsFile)
                {
                    using var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(snapshot, Json));
                    content.Headers.ContentType = new("application/json");
                    using var response = await client.PostAsync(uri, content, token);
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException($"Localization publication failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync(token)}. GitHub Pages is read-only; use --index in a checkout and commit the history.");
                    result = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(token));
                }
                else
                    result = await publisher.PublishSnapshotAsync(snapshot, index, token);
            }
        }
        await output.WriteLineAsync(JsonSerializer.Serialize(result, Json));
    }
}
