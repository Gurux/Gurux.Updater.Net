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

/// <summary>Runs Docker with separately escaped arguments, outside the AMI container.</summary>
public interface IGXComposeRunner
{
    /// <summary>Runs a Docker operation with separately supplied arguments.</summary>
    Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken token = default);
}

/// <summary>Uses the host Docker CLI and its existing registry credentials.</summary>
public sealed class GXComposeProcessRunner(string workingDirectory, int timeoutSeconds = 600) : IGXComposeRunner
{
    /// <inheritdoc/>
    public async Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var start = new ProcessStartInfo("docker") { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Docker could not be started.");
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            string result = await output;
            _ = await error;
            // Do not return effective environment/configuration or credentials as browser-visible errors.
            if (process.ExitCode != 0)
                throw new IOException($"Docker {arguments[0]} failed (exit {process.ExitCode}). See host Docker logs.");
            return result;
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            try
            { await Task.WhenAll(output, error); }
            catch { }
            throw;
        }
    }
}
