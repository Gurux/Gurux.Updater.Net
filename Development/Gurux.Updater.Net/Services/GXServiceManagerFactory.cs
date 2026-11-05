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

namespace Gurux.Updater.Services;
/// <summary>
/// Creates a service manager for the current operating system.
/// </summary>
public static class GXServiceManagerFactory
{
    /// <summary>
    /// Creates a Windows or systemd service manager, or throws on an unsupported operating system.
    /// </summary>
    public static IGXServiceManager Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new GXWindowsServiceManager();
        }
        if (OperatingSystem.IsLinux())
        {
            return new GXSystemdServiceManager();
        }
        throw new PlatformNotSupportedException("Service updates are supported on Windows and Linux.");
    }
}
