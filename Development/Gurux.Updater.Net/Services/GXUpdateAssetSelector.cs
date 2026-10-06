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
using System.Runtime.InteropServices;

namespace Gurux.Updater.Services;

internal static class GXUpdateAssetSelector
{
    public static GXUpdateAsset? SelectAsset(IReadOnlyList<GXUpdateAsset> assets, string? pattern)
    {
        if (!string.IsNullOrWhiteSpace(pattern))
        {
            return assets.FirstOrDefault(a => WildcardMatch(a.Name, pattern));
        }
        string os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "osx";
        string arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        string[] names = arch == "x64" ? ["x64", "amd64"] : arch == "arm64" ? ["arm64", "aarch64"] : [arch];
        return assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            && a.Name.Contains(os, StringComparison.OrdinalIgnoreCase)
            && names.Any(n => a.Name.Contains(n, StringComparison.OrdinalIgnoreCase)))
            ?? assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    private static bool WildcardMatch(string value, string pattern)
    {
        string[] parts = pattern.Split('*');
        int position = 0;
        foreach (string part in parts)
        {
            if (part.Length == 0)
            {
                continue;
            }
            int index = value.IndexOf(part, position, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }
            position = index + part.Length;
        }
        return true;
    }
}