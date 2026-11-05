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