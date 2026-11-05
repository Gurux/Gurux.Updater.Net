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

namespace Gurux.Updater.Services;

/// <summary>Strict shared publication protocol and explicit source configuration reader.</summary>
public static class GXLocalizationPublicationIO
{
    /// <summary>Reads the server publication request, rejecting duplicate keys.</summary>
    public static GXLocalizationSnapshot ReadSnapshot(byte[] bytes) => GXLocalizationJson.Read<GXLocalizationSnapshot>(bytes, "publication snapshot");

    /// <summary>Reads { sources: [...] }; directory/project paths are relative to the configuration file.</summary>
    public static List<GXLocalizationSource> ReadSources(string path)
    {
        var configuration = GXLocalizationJson.Read<SourceConfiguration>(File.ReadAllBytes(path), path);
        if (configuration.Sources == null)
            throw new InvalidDataException("sources must be an array.");
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        foreach (var source in configuration.Sources)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.Directory))
                throw new InvalidDataException("Each localization source requires directory.");
            source.Directory = Path.GetFullPath(source.Directory, directory);
            if (source.Project != null)
                source.Project = Path.GetFullPath(source.Project, directory);
            if (source.MetadataPackage != null)
                source.MetadataPackage = Path.GetFullPath(source.MetadataPackage, directory);
        }
        return configuration.Sources;
    }

    private sealed class SourceConfiguration
    {
        [System.Text.Json.Serialization.JsonRequired]
        public List<GXLocalizationSource> Sources { get; init; } = [];
    }
}
