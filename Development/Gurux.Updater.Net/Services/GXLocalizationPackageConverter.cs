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
using System.Text.Json.Serialization;
using Gurux.Updater.Model;

namespace Gurux.Updater.Services;

/// <summary>Preserves the v1 library resource map while reading/writing flat v2 wire resources.</summary>
public sealed class GXLocalizationPackageConverter : JsonConverter<GXLocalizationPackage>
{
    /// <inheritdoc/>
    public override GXLocalizationPackage Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        GXLocalizationJson.CheckDuplicateProperties(root, "$", "package");
        int schema = root.GetProperty("schemaVersion").GetInt32();
        string Required(string name) => root.GetProperty(name).GetString() ?? throw new JsonException($"{name} is required.");
        string? Optional(string name) => root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
        string? culture = Optional("culture");
        var resources = root.GetProperty("resources");
        var map = schema == 2
            ? new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal) { [culture ?? throw new JsonException("culture is required.")] = resources.Deserialize<Dictionary<string, string>>(options)! }
            : resources.Deserialize<Dictionary<string, Dictionary<string, string>>>(options)!;
        return new GXLocalizationPackage { SchemaVersion = schema, Culture = culture, Owner = Required("owner"), OwnerType = Required("ownerType"),
            Version = Required("version"), DefaultCulture = Required("defaultCulture"), OwnerVersionRange = Optional("ownerVersionRange"),
            Description = Optional("description"), ReleaseNotes = Optional("releaseNotes"), Resources = map };
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, GXLocalizationPackage value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", value.SchemaVersion);
        writer.WriteString("owner", value.Owner); writer.WriteString("ownerType", value.OwnerType);
        if (value.Culture != null) writer.WriteString("culture", value.Culture);
        writer.WriteString("version", value.Version);
        if (value.OwnerVersionRange != null) writer.WriteString("ownerVersionRange", value.OwnerVersionRange);
        writer.WriteString("defaultCulture", value.DefaultCulture);
        if (value.Description != null) writer.WriteString("description", value.Description);
        if (value.ReleaseNotes != null) writer.WriteString("releaseNotes", value.ReleaseNotes);
        writer.WritePropertyName("resources");
        if (value.SchemaVersion == 2) JsonSerializer.Serialize(writer, value.Resources[value.Culture!], options);
        else JsonSerializer.Serialize(writer, value.Resources, options);
        writer.WriteEndObject();
    }
}
