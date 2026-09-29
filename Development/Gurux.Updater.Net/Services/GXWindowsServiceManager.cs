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

namespace Gurux.Updater.Services;

/// <summary>
/// Manages Windows services through sc.exe.
/// </summary>
public sealed class GXWindowsServiceManager : IGXServiceManager
{
    /// <summary>
    /// Stops the named service and waits for it to stop.
    /// </summary>
    public async Task StopAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        EnsurePlatform();
        (int code, string output) = await GXProcessRunner.RunAsync("sc.exe", $"stop \"{serviceName}\"", cancellationToken);
        if (code != 0 && !output.Contains("SERVICE_NOT_ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unable to stop Windows service '{serviceName}': {output.Trim()}");
        }
        await WaitForStateAsync(serviceName, false, cancellationToken);
    }

    /// <summary>
    /// Starts the named service and waits for it to run.
    /// </summary>
    public async Task StartAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        EnsurePlatform();
        (int code, string output) = await GXProcessRunner.RunAsync("sc.exe", $"start \"{serviceName}\"", cancellationToken);
        if (code != 0 && !output.Contains("SERVICE_ALREADY_RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unable to start Windows service '{serviceName}': {output.Trim()}");
        }
        await WaitForStateAsync(serviceName, true, cancellationToken);
    }

    /// <summary>
    /// Checks whether the named service is currently running.
    /// </summary>
    public async Task<bool> IsRunningAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        EnsurePlatform();
        (int code, string output) = await GXProcessRunner.RunAsync("sc.exe", $"query \"{serviceName}\"", cancellationToken);
        return code == 0 && output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
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
        throw new TimeoutException($"Windows service '{serviceName}' did not reach the expected state.");
    }
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


    /// <summary>
    /// Throws if the current operating system does not support this service manager.
    /// </summary>
    private static void EnsurePlatform()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows service management requires Windows.");
        }
    }
}
