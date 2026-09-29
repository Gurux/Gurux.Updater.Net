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
