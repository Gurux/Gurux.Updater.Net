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
using Gurux.Updater.Model;
using Gurux.Updater.Services;

namespace Gurux.Updater.Tool;

/// <summary>File protocol used by git/GitHub transport; all package semantics stay in C#.</summary>
internal static class GXContentCommand
{
    internal static async Task RunAsync(HttpClient client, string[] args, TextWriter output)
    {
        if (args.Length == 0)
            throw new ArgumentException("content prepare|rebuild|verify-manifest|verify|import|validate-resources --sources <json> --history <json> --manifest <json> --package <file> --output <directory> --catalog-root <directory>");
        string command = args[0];
        var allowed = command switch
        {
            "prepare" => new[] { "--sources", "--history", "--output" },
            "rebuild" => ["--sources", "--manifest", "--output"],
            "verify" => ["--manifest", "--package"],
            "verify-manifest" => ["--manifest"],
            "import" => ["--manifest", "--package", "--catalog-root"],
            "validate-resources" => ["--catalog-root"],
            _ => throw new ArgumentException("Unknown content command.")
        };
        var options = new Dictionary<string, string>();
        for (int pos = 1; pos < args.Length; pos += 2)
            if (pos + 1 >= args.Length || !allowed.Contains(args[pos]) || !options.TryAdd(args[pos], args[pos + 1]))
                throw new ArgumentException("Unknown, duplicate or incomplete content option.");
        string Get(string name) => options.GetValueOrDefault(name) ?? throw new ArgumentException($"{name} is required.");
        var service = new GXContentReleaseService(client);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true, NewLine = "\n" };
        GXContentManifest Manifest() => GXContentReleaseService.ReadManifest(File.ReadAllBytes(Get("--manifest")));
        object result;
        if (command == "prepare" || command == "rebuild")
        {
            var sources = GXContentReleaseService.ReadSources(Get("--sources"));
            List<string> warnings = [];
            var packages = command == "prepare" ? await service.PrepareAsync(sources, GXContentReleaseService.ReadHistory(File.ReadAllBytes(Get("--history"))), warnings: warnings)
                : [await service.RebuildAsync(sources, Manifest())];
            foreach (string warning in warnings)
                await Console.Error.WriteLineAsync(warning);
            var entries = new List<object>();
            int ordinal = 0;
            foreach (var package in packages)
            {
                string directory = Path.GetFullPath(Path.Combine(Get("--output"), (ordinal++).ToString("D4")));
                Directory.CreateDirectory(directory);
                string manifest = Path.Combine(directory, "release-manifest.json"), artifact = Path.Combine(directory, package.Manifest.Package);
                await File.WriteAllBytesAsync(manifest, JsonSerializer.SerializeToUtf8Bytes(package.Manifest, json));
                await File.WriteAllBytesAsync(artifact, package.Bytes);
                entries.Add(new { manifest, package = artifact, tag = package.Manifest.Tag });
            }
            result = entries;
        }
        else if (command == "verify-manifest")
        {
            var manifest = Manifest();
            result = new { valid = true, tag = manifest.Tag };
        }
        else if (command == "validate-resources")
        {
            await GXContentReleaseService.ValidateResourceStoreAsync(Get("--catalog-root"));
            result = new { valid = true };
        }
        else
        {
            var manifest = Manifest();
            var bytes = await File.ReadAllBytesAsync(Get("--package"));
            GXContentReleaseService.ValidatePackage(manifest, bytes);
            if (command == "import")
                await service.ImportAsync(manifest, bytes, Get("--catalog-root"));
            result = new { valid = true, tag = manifest.Tag };
        }
        await output.WriteLineAsync(JsonSerializer.Serialize(result, json));
    }
}
