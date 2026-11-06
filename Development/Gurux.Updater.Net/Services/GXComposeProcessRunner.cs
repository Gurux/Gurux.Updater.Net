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
        {
            start.ArgumentList.Add(argument);
        }
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
            {
                throw new IOException($"Docker {arguments[0]} failed (exit {process.ExitCode}). See host Docker logs.");
            }
            return result;
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            try
            { await Task.WhenAll(output, error); }
            catch { }
            throw;
        }
    }
}
