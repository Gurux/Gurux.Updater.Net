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

using System.Diagnostics;

namespace Gurux.Updater.Services;

/// <summary>
/// Starts and stops services using Windows or Linux service commands.
/// </summary>
internal sealed class GXServiceManager
{
    /// <summary>
    /// Stops the named service and waits for it to stop.
    /// </summary>
    public async Task StopAsync(string serviceName, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            await RunAsync("sc.exe", $"stop \"{serviceName}\"", true, cancellationToken);
            await WaitForStateAsync(serviceName, false, cancellationToken);
        }
        else if (OperatingSystem.IsLinux())
        {
            await RunAsync("systemctl", $"stop \"{serviceName}\"", false, cancellationToken);
        }
        else
        {
            throw new PlatformNotSupportedException("Service updates are supported on Windows and Linux.");
        }
    }

    /// <summary>
    /// Starts the named service and waits for it to run.
    /// </summary>
    public async Task StartAsync(string serviceName, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            await RunAsync("sc.exe", $"start \"{serviceName}\"", false, cancellationToken);
            await WaitForStateAsync(serviceName, true, cancellationToken);
        }
        else if (OperatingSystem.IsLinux())
        {
            await RunAsync("systemctl", $"start \"{serviceName}\"", false, cancellationToken);
        }
        else
        {
            throw new PlatformNotSupportedException("Service updates are supported on Windows and Linux.");
        }
    }

    /// <summary>
    /// Polls the service until it reaches the requested running state or the retry limit is exceeded.
    /// </summary>
    private static async Task WaitForStateAsync(string serviceName, bool running, CancellationToken cancellationToken)
    {
        for (int pos = 0; pos != 30; ++pos)
        {
            (int exitCode, string output) = await RunCaptureAsync("sc.exe", $"query \"{serviceName}\"", cancellationToken);
            if (exitCode == 0 && output.Contains(running ? "RUNNING" : "STOPPED", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            await Task.Delay(1000, cancellationToken);
        }
        throw new TimeoutException($"Service '{serviceName}' did not reach the expected state.");
    }

    /// <summary>
    /// Runs a service command and checks its exit code, optionally accepting an already stopped service.
    /// </summary>
    private static async Task RunAsync(string fileName, string arguments, bool allowAlreadyStopped, CancellationToken cancellationToken)
    {
        (int exitCode, string output) = await RunCaptureAsync(fileName, arguments, cancellationToken);
        if (exitCode != 0 && !(allowAlreadyStopped && output.Contains("SERVICE_NOT_ACTIVE", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"{fileName} {arguments} failed with exit code {exitCode}: {output.Trim()}");
        }
    }

    /// <summary>
    /// Runs an external command and returns its exit code and combined standard output and error.
    /// </summary>
    private static async Task<(int ExitCode, string Output)> RunCaptureAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException($"Unable to start {fileName}.");
        string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, stdout + stderr);
    }
}
