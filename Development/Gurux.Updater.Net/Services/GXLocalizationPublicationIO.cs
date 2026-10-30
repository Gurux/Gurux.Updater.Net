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
