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
using Gurux.Updater.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gurux.Updater.Tool;

/// <summary>Publishes deployment ZIPs and complete localization JSON packages through HTTP or HTTPS.</summary>
public static class GXCatalogPublishCommand
{
    /// <summary>Reads package metadata, applies overrides, uploads the ZIP, and writes the updated catalog or status.</summary>
    public static async Task RunAsync(HttpClient client, GXUpdaterOptions options, TextWriter output,
        CancellationToken cancellationToken = default)
    {
        var packagePath = options.PublishPackage ?? throw new ArgumentException("A ZIP or localization JSON package is required.");
        if (Path.GetExtension(packagePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            if (options.Product != null || options.PackageType != null || options.PackageVersion != null || options.IncludePrereleases)
                throw new ArgumentException("Localization identity and version must be specified in the JSON package, without ZIP metadata overrides.");
            var service = new GXLocalizationService(client, allowHttp: true);
            var package = await service.ReadPackageAsync(packagePath, cancellationToken);
            var index = await GXLocalizationCommand.ResolveIndexAsync(client, options.CatalogUrl, cancellationToken);
            using var content = new ByteArrayContent(await File.ReadAllBytesAsync(packagePath, cancellationToken));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            using var response = await client.PostAsync(index, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Publishing localization '{package.Owner}' version '{package.Version}' failed: HTTP {(int)response.StatusCode}. {await response.Content.ReadAsStringAsync(cancellationToken)}", null, response.StatusCode);
            await output.WriteLineAsync(options.Json ? await response.Content.ReadAsStringAsync(cancellationToken)
                : response.StatusCode == System.Net.HttpStatusCode.OK
                    ? $"Already published: {package.Owner} {package.Version}"
                    : $"Published localization '{package.Owner}' version '{package.Version}' to {index}");
            return;
        }
        GXCatalogPackageInfo info;
        try
        {
            info = GXCatalogPackageReader.Read(packagePath);
        }
        catch (InvalidDataException) when (options.Product != null && options.PackageType.HasValue && options.PackageVersion != null)
        {
            info = new GXCatalogPackageInfo { Id = options.Product, Name = options.Product, Type = options.PackageType.Value, Version = options.PackageVersion };
        }
        if (options.Product != null)
        {
            info.Id = options.Product;
        }

        if (options.PackageType.HasValue)
        {
            info.Type = options.PackageType.Value;
        }

        if (options.PackageVersion != null)
        {
            info.Version = options.PackageVersion;
        }

        info.IsPrerelease = options.IncludePrereleases || info.Version.Split('+')[0].Contains('-');
        if (string.IsNullOrWhiteSpace(info.Id))
        {
            throw new ArgumentException("The package product ID could not be determined. Specify --product <id>.");
        }

        if (string.IsNullOrWhiteSpace(info.Version))
        {
            throw new ArgumentException("The package release version could not be determined. Specify --version <version>.");
        }

        var catalog = await new GXCatalogUpdateService(client).PublishAsync(options.CatalogUrl, packagePath, info, cancellationToken);
        if (options.Json)
        {
            var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
            json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            await output.WriteLineAsync(JsonSerializer.Serialize(catalog, json));
        }
        else
        {
            await output.WriteLineAsync($"Published {info.Type} '{info.Id}' version '{info.Version}' to {options.CatalogUrl}");
        }
    }
}
