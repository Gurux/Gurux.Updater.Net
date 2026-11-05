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

using Gurux.Updater.Enums;
using Gurux.Updater.Model;
using Gurux.Updater.Services;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Gurux.Updater.Tool;

/// <summary>
/// Provides the command-line entry point and update installation workflow.
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// Executes the requested updater command and returns its exit code, 
    /// reporting errors to standard error.
    /// </summary>
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "deployment-agent")
            {
                using var cancellation = new CancellationTokenSource();
                ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
                Console.CancelKeyPress += cancel;
                using var sigterm = OperatingSystem.IsLinux() ? System.Runtime.InteropServices.PosixSignalRegistration.Create(
                    System.Runtime.InteropServices.PosixSignal.SIGTERM, context => { context.Cancel = true; cancellation.Cancel(); }) : null;
                try
                { await GXDeploymentCommand.RunAsync(args[1..], cancellation.Token); return 0; }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return 130; }
                finally { Console.CancelKeyPress -= cancel; }
            }
            if (args.Length > 0 && args[0] == "register-module")
            {
                await GXRegisterModuleCommand.RunAsync(args[1..], Console.Out);
                return 0;
            }
            if (args.Length > 0 && args[0] == "content")
            {
                using var contentClient = new HttpClient();
                await GXContentCommand.RunAsync(contentClient, args[1..], Console.Out);
                return 0;
            }
            if (args.Length > 0 && args[0] == "script")
            {
                using var cancellation = new CancellationTokenSource();
                ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
                Console.CancelKeyPress += cancel;
                try
                {
                    using var scriptClient = new HttpClient();
                    scriptClient.DefaultRequestHeaders.UserAgent.ParseAdd("Gurux.Updater/1.0");
                    await GXScriptCommand.RunAsync(scriptClient, args[1..], Console.Out, cancellation.Token);
                    return 0;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return 130; }
                finally { Console.CancelKeyPress -= cancel; }
            }
            if (args.Length == 2 && args[0] == "package-metadata")
            {
                Console.WriteLine(JsonSerializer.Serialize(GXCatalogPackageReader.Read(args[1]), JsonOptions));
                return 0;
            }
            if (args.Length > 0 && args[0] == "localization")
            {
                using var cancellation = new CancellationTokenSource();
                ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
                Console.CancelKeyPress += cancel;
                try
                {
                    using var localizationClient = new HttpClient();
                    localizationClient.DefaultRequestHeaders.UserAgent.ParseAdd("Gurux.Updater/1.0");
                    await GXLocalizationCommand.RunAsync(localizationClient, args[1..], Console.Out, cancellation.Token, Console.Error);
                    return 0;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    Console.Error.WriteLine("Localization operation cancelled.");
                    return 130;
                }
                finally
                {
                    Console.CancelKeyPress -= cancel;
                }
            }
            GXUpdaterOptions options = GXUpdaterOptions.Parse(args);
            if (options.ShowHelp)
            {
                Console.WriteLine(GXUpdaterOptions.HelpText);
                return 0;
            }
            if (options.PublishPackage != null)
            {
                using var catalogClient = new HttpClient();
                catalogClient.DefaultRequestHeaders.UserAgent.ParseAdd("Gurux.Updater/1.0");
                await GXCatalogPublishCommand.RunAsync(catalogClient, options, Console.Out);
                return 0;
            }
            if (options.ListLocalization || options.Localization != null)
            {
                using var cancellation = new CancellationTokenSource();
                ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
                Console.CancelKeyPress += cancel;
                try
                {
                    using var catalogClient = new HttpClient();
                    catalogClient.DefaultRequestHeaders.UserAgent.ParseAdd("Gurux.Updater/1.0");
                    await GXLocalizationCommand.RunAsync(catalogClient, options, Console.Out, cancellation.Token, Console.Error);
                    return 0;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    Console.Error.WriteLine("Localization operation cancelled.");
                    return 130;
                }
                finally
                {
                    Console.CancelKeyPress -= cancel;
                }
            }
            if (options.ListManufacturerSettings)
            {
                using var catalogClient = new HttpClient();
                catalogClient.DefaultRequestHeaders.UserAgent.ParseAdd("Gurux.Updater/1.0");
                await GXManufacturerSettingsCommand.RunAsync(catalogClient, options, Console.Out);
                return 0;
            }
            if (options.ListCatalog)
            {
                using var catalogClient = new HttpClient();
                catalogClient.DefaultRequestHeaders.UserAgent.ParseAdd("Gurux.Updater/1.0");
                if (options.CurrentVersion != null)
                    return await GXCatalogCommand.CheckAsync(catalogClient, options, Console.Out);
                await GXCatalogCommand.RunAsync(catalogClient, options, Console.Out);
                return 0;
            }
            using HttpClient client = CreateHttpClient(options.Token);
            GXGitHubUpdateService service = new(client);
            return options.Command switch
            {
                "check" => await CheckAsync(service, options),
                "update" => await UpdateAsync(service, options),
                _ => throw new ArgumentException($"Unknown command '{options.Command}'.")
            };
        }
        catch (Exception ex)
        {
            if (args.Length >= 2 && args[0] == "localization" && args[1] is "publish-snapshot" or "publish-package" &&
                ex is InvalidDataException or InvalidOperationException or ArgumentException)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { valid = false, error = ex.Message }));
                return ex is InvalidOperationException ? 3 : 2;
            }
            if (args.Length >= 2 && args[0] == "script" && args[1] == "validate-manifest" && ex is InvalidDataException or JsonException)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { valid = false, error = ex.Message }));
                return 2;
            }
            if (args.Length >= 2 && args[0] == "localization" && args[1] == "validate-package" && ex is InvalidDataException)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { valid = false, error = ex.Message }));
                return 2;
            }
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// Creates an HTTP client with GitHub API headers and an optional bearer token.
    /// </summary>
    private static HttpClient CreateHttpClient(string? token)
    {
        HttpClient client = new();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Gurux.Updater/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    /// <summary>
    /// Creates an application update target from the command-line options.
    /// </summary>
    private static GXUpdateTarget CreateTarget(
    GXUpdaterOptions options)
    {
        return new GXUpdateTarget
        {
            Name = Path.GetFileNameWithoutExtension(
                options.Application!),
            Type = UpdateTargetType.Application,
            Application = options.Application!,
            Repository = options.Repository!,
            AssetPattern = options.AssetPattern
        };
    }

    /// <summary>
    /// Checks one or more targets, writes the results, and returns the corresponding exit code.
    /// </summary>
    private static async Task<int> CheckAsync(GXGitHubUpdateService service, GXUpdaterOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Targets))
        {
            string json = await File.ReadAllTextAsync(options.Targets);
            GXUpdateTarget[] targets = JsonSerializer.Deserialize<GXUpdateTarget[]>(json, JsonOptions)
                ?? throw new InvalidDataException("Targets file is empty.");
            IReadOnlyList<GXUpdateCheckResult> results = await service.CheckAsync(
                targets, options.MaxConcurrency, CancellationToken.None);
            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(results, JsonOptions));
            }
            else
            {
                foreach (GXUpdateCheckResult result in results)
                {
                    if (!result.Succeeded)
                    {
                        Console.WriteLine($"{result.Name,-28} {result.Type,-11} Update check failed: {result.Error}");
                    }
                    else
                    {
                        GXUpdateInfo update = result.Update!;
                        string status = $"{GXUpdateText.Status(update)} ({update.CurrentVersion} -> {update.LatestVersion})";
                        Console.WriteLine($"{result.Name,-28} {result.Type,-11} {status}");
                    }
                }
            }
            return results.Any(x => x.Succeeded && x.Update!.UpdateAvailable) ? 10 :
                results.Any(x => !x.Succeeded) ? 1 : 0;
        }
        GXUpdateTarget target = CreateTarget(options);
        GXUpdateInfo info;
        try
        { info = await service.CheckAsync(target, CancellationToken.None); }
        catch (Exception ex)
        {
            info = new GXUpdateInfo
            {
                Name = target.Name ?? string.Empty,
                Application = target.Application ?? string.Empty,
                Repository = target.Repository ?? string.Empty,
                Status = GXUpdateStatus.CheckFailed,
                Error = ex.Message
            };
        }
        if (options.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(info, JsonOptions));
        }
        else
        {
            Console.WriteLine(GXUpdateText.Status(info));
            Console.WriteLine($"Priority:         {info.Priority}");
            Console.WriteLine($"Security update:  {info.IsSecurityUpdate}");
            if (info.Error is not null)
                Console.Error.WriteLine(info.Error);
            Console.WriteLine($"Application:      {info.Application}");
            Console.WriteLine($"Current version:  {info.CurrentVersion}");
            Console.WriteLine($"Latest version:   {info.LatestVersion}");
            Console.WriteLine($"Update available: {(info.UpdateAvailable ? "Yes" : "No")}");
            Console.WriteLine($"Container:        {(info.IsContainer ? "Yes" : "No")}");
            Console.WriteLine($"Release:          {info.ReleaseUrl}");
            if (info.Asset is not null)
            {
                Console.WriteLine($"Asset:            {info.Asset.Name}");
            }
        }
        return info.Status == GXUpdateStatus.CheckFailed ? 1 : info.UpdateAvailable ? 10 : 0;
    }

    /// <summary>
    /// Downloads and installs an available update, with optional restart checks and backup restoration on installation failure.
    /// </summary>
    private static async Task<int> UpdateAsync(GXGitHubUpdateService service, GXUpdaterOptions options)
    {
        bool local = options.LocalPackage != null;
        GXUpdateInfo? info = local ? null : await service.CheckAsync(CreateTarget(options), CancellationToken.None);
        if (!local && !info!.UpdateAvailable)
        {
            Console.WriteLine("The application is already up to date.");
            return 0;
        }
        if (!local && info!.IsContainer)
        {
            Console.WriteLine($"Version {info.LatestVersion} is available.");
            Console.WriteLine("In-place updates are disabled in containers. Deploy the new container image instead.");
            return 20;
        }
        if (!local && info!.Asset is null)
        {
            throw new InvalidOperationException("No compatible .zip release asset was found.");
        }

        string? applicationPath = string.IsNullOrWhiteSpace(options.Application) ? null : Path.GetFullPath(options.Application);
        string targetDirectory = local ? Path.GetFullPath(options.Destination!) : Path.GetDirectoryName(applicationPath!)!;
        if (local)
        {
            if (string.Equals(Path.TrimEndingDirectorySeparator(targetDirectory), Path.GetPathRoot(targetDirectory), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The destination must not be a filesystem root.");
            }

            if (applicationPath != null)
            {
                string relative = Path.GetRelativePath(targetDirectory, applicationPath);
                if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
                {
                    throw new ArgumentException("--application must be inside --destination.");
                }
            }
        }
        bool targetExisted = Directory.Exists(targetDirectory);

        string tempRoot = Path.Combine(Path.GetTempPath(), "Gurux.Updater", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        IGXServiceManager? serviceManager = string.IsNullOrWhiteSpace(options.Service)
            ? null
            : GXServiceManagerFactory.Create();
        using HttpClient healthClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        GXHealthChecker healthChecker = new(healthClient);
        string? backupDirectory = null;
        bool serviceStopped = false;
        Process? applicationProcess = null;
        try
        {
            string package = local ? Path.GetFullPath(options.LocalPackage!) : Path.Combine(tempRoot, info!.Asset!.Name);
            if (!local)
            {
                Console.WriteLine($"Downloading {info!.Asset!.Name}...");
                GXConsoleProgress? progress = options.Json ? null : new GXConsoleProgress();
                try
                {
                    await service.DownloadAsync(
                        info.Asset.DownloadUrl,
                        package,
                        progress,
                        CancellationToken.None);
                }
                finally
                {
                    progress?.Complete();
                }
            }
            if (!string.IsNullOrWhiteSpace(options.Sha256))
            {
                VerifySha256(package, options.Sha256);
                Console.WriteLine("SHA-256 verified.");
            }

            backupDirectory = Path.TrimEndingDirectorySeparator(targetDirectory) + ".backup";
            string extractDirectory = Path.Combine(tempRoot, "package");
            Directory.CreateDirectory(extractDirectory);
            ZipFile.ExtractToDirectory(package, extractDirectory, true);
            if (local && applicationPath != null && !File.Exists(Path.Combine(extractDirectory, Path.GetRelativePath(targetDirectory, applicationPath))))
            {
                throw new InvalidDataException("The local ZIP does not contain the specified application.");
            }

            if (!string.IsNullOrWhiteSpace(options.Service))
            {
                Console.WriteLine($"Stopping service {options.Service}...");
                await serviceManager!.StopAsync(options.Service, CancellationToken.None);
                serviceStopped = true;
            }
            else if (options.ProcessId is int processId)
            {
                await WaitForProcessAsync(processId, options.WaitSeconds);
            }

            if (targetExisted)
            {
                BackupDirectory(targetDirectory, backupDirectory);
            }

            try
            {
                CopyDirectory(extractDirectory, targetDirectory);
                if (!options.NoRestart)
                {
                    if (!string.IsNullOrWhiteSpace(options.Service))
                    {
                        Console.WriteLine($"Starting service {options.Service}...");
                        await serviceManager!.StartAsync(options.Service, CancellationToken.None);
                        serviceStopped = false;
                    }
                    else if (File.Exists(applicationPath))
                    {
                        applicationProcess = StartApplication(applicationPath, targetDirectory);
                    }
                    await healthChecker.WaitAsync(options.HealthUrl, options.VersionUrl, info?.LatestVersion ?? string.Empty, TimeSpan.FromSeconds(options.StartupTimeout), CancellationToken.None);
                }
                Console.WriteLine(local ? "Installed local package." : $"Installed version {info!.LatestVersion}.");
            }
            catch
            {
                Console.Error.WriteLine("Update failed. Restoring the previous version...");
                if (applicationProcess != null)
                {
                    try
                    {
                        if (!applicationProcess.HasExited)
                        {
                            applicationProcess.Kill(entireProcessTree: true);
                            await applicationProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                        }
                    }
                    catch (InvalidOperationException) when (applicationProcess.HasExited) { }
                    finally { applicationProcess.Dispose(); applicationProcess = null; }
                }
                if (!string.IsNullOrWhiteSpace(options.Service) && !serviceStopped)
                {
                    try
                    {
                        await serviceManager!.StopAsync(options.Service, CancellationToken.None);
                    }
                    catch
                    {
                    }
                    serviceStopped = true;
                }
                if (targetExisted)
                {
                    RestoreDirectory(targetDirectory, backupDirectory);
                }
                else if (Directory.Exists(targetDirectory))
                {
                    Directory.Delete(targetDirectory, true);
                }

                if (!options.NoRestart && !string.IsNullOrWhiteSpace(options.Service))
                {
                    await serviceManager!.StartAsync(options.Service, CancellationToken.None);
                    serviceStopped = false;
                    await healthChecker.WaitAsync(options.HealthUrl, null, info?.CurrentVersion ?? string.Empty, TimeSpan.FromSeconds(options.StartupTimeout), CancellationToken.None);
                }
                else if (!options.NoRestart && targetExisted && File.Exists(applicationPath))
                {
                    applicationProcess = StartApplication(applicationPath, targetDirectory);
                    await healthChecker.WaitAsync(options.HealthUrl, null, info?.CurrentVersion ?? string.Empty,
                        TimeSpan.FromSeconds(options.StartupTimeout), CancellationToken.None);
                }
                throw;
            }
        }
        finally
        {
            applicationProcess?.Dispose();
            if (serviceStopped && !options.NoRestart && !string.IsNullOrWhiteSpace(options.Service))
            {
                try
                {
                    await serviceManager!.StartAsync(options.Service, CancellationToken.None);
                }
                catch
                {
                }
            }
            try
            {
                Directory.Delete(tempRoot, true);
            }
            catch
            {
            }
        }
        return 0;
    }

    private static Process StartApplication(string applicationPath, string workingDirectory) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = applicationPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        }) ?? throw new IOException("The application process could not be started.");

    /// <summary>
    /// Throws if the file SHA-256 hash differs from the expected hexadecimal value.
    /// </summary>
    private static void VerifySha256(string file, string expected)
    {
        using FileStream stream = File.OpenRead(file);
        string actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(expected.Replace("-", string.Empty), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"SHA-256 mismatch. Expected {expected}, actual {actual}.");
        }
    }

    /// <summary>
    /// Waits for the specified process to exit within the timeout, returning immediately if it no longer exists.
    /// </summary>
    private static async Task WaitForProcessAsync(int processId, int seconds)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(seconds));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (ArgumentException)
        {
        }
    }

    /// <summary>
    /// Replaces any existing backup directory with a recursive copy of the source directory.
    /// </summary>
    private static void BackupDirectory(string source, string backup)
    {
        if (Directory.Exists(backup))
        {
            Directory.Delete(backup, true);
        }
        CopyDirectory(source, backup);
    }

    /// <summary>
    /// Replaces the target directory with the backup contents when the backup exists.
    /// </summary>
    private static void RestoreDirectory(string target, string backup)
    {
        if (!Directory.Exists(backup))
        {
            return;
        }
        if (Directory.Exists(target))
        {
            Directory.Delete(target, true);
        }
        CopyDirectory(backup, target);
    }

    /// <summary>
    /// Recursively copies files and directories, overwriting existing destination files.
    /// </summary>
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        }
        foreach (string directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
