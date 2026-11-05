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
