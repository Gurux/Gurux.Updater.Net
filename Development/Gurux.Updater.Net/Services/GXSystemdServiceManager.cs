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
/// Manages Linux systemd services through systemctl.
/// </summary>
public sealed class GXSystemdServiceManager : IGXServiceManager
{
    /// <summary>
    /// Stops the named service and waits for it to stop.
    /// </summary>
    public async Task StopAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        EnsurePlatform();
        await RunRequiredAsync("stop", serviceName, cancellationToken);
        await WaitForStateAsync(serviceName, false, cancellationToken);
    }

    /// <summary>
    /// Starts the named service and waits for it to run.
    /// </summary>
    public async Task StartAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        EnsurePlatform();
        await RunRequiredAsync("start", serviceName, cancellationToken);
        await WaitForStateAsync(serviceName, true, cancellationToken);
    }

    /// <summary>
    /// Checks whether the named service is currently running.
    /// </summary>
    public async Task<bool> IsRunningAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        EnsurePlatform();
        (int code, _) = await GXProcessRunner.RunAsync("systemctl", $"is-active --quiet \"{serviceName}\"", cancellationToken);
        return code == 0;
    }

    /// <summary>
    /// Runs a systemctl command and throws if it returns a nonzero exit code.
    /// </summary>
    private static async Task RunRequiredAsync(string command, string serviceName, CancellationToken cancellationToken)
    {
        (int code, string output) = await GXProcessRunner.RunAsync("systemctl", $"{command} \"{serviceName}\"", cancellationToken);
        if (code != 0)
        {
            throw new InvalidOperationException($"Unable to {command} systemd service '{serviceName}': {output.Trim()}");
        }
    }

    /// <summary>
    /// Polls the service until it reaches the requested running state or the retry limit is exceeded.
    /// </summary>
    private async Task WaitForStateAsync(string serviceName, bool running, CancellationToken cancellationToken)
    {
        for (int pos = 0; pos != 60; ++pos)
        {
            if (await IsRunningAsync(serviceName, cancellationToken) == running)
            {
                return;
            }
            await Task.Delay(1000, cancellationToken);
        }
        throw new TimeoutException($"systemd service '{serviceName}' did not reach the expected state.");
    }


    /// <summary>
    /// Throws if the current operating system does not support this service manager.
    /// </summary>
    private static void EnsurePlatform()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("systemd service management requires Linux.");
        }
    }
}
