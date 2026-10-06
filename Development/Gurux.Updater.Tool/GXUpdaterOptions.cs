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

namespace Gurux.Updater.Tool;

/// <summary>
/// Contains parsed command-line options for checking and installing updates.
/// </summary>
public sealed class GXUpdaterOptions
{
    /// <summary>
    /// Gets the command to execute, either check or update.
    /// </summary>
    public string Command { get; private set; } = "check";
    /// <summary>
    /// Gets the path to the application or add-in whose version is checked.
    /// </summary>
    public string? Application { get; private set; }
    /// <summary>
    /// Gets the path to the JSON file containing targets for a batch check.
    /// </summary>
    public string? Targets { get; private set; }
    /// <summary>
    /// Gets the maximum number of concurrent update checks; the default is four.
    /// </summary>
    public int MaxConcurrency { get; private set; } = 4;
    /// <summary>
    /// Gets the GitHub repository in owner/name format.
    /// </summary>
    public string Repository { get; private set; } = string.Empty;
    /// <summary>
    /// Gets the case-insensitive asset name pattern, whose parts are separated by asterisks.
    /// </summary>
    public string? AssetPattern { get; private set; }
    /// <summary>
    /// Gets the optional GitHub bearer token.
    /// </summary>
    public string? Token { get; private set; }
    /// <summary>
    /// Gets the expected SHA-256 hash of the downloaded package, if specified.
    /// </summary>
    public string? Sha256 { get; private set; }
    /// <summary>
    /// Gets the identifier of the process to wait for before installing an update.
    /// </summary>
    public int? ProcessId { get; private set; }
    /// <summary>
    /// Gets the Windows or systemd service name to stop and start during installation.
    /// </summary>
    public string? Service { get; private set; }
    /// <summary>
    /// Gets the optional HTTP endpoint that must return a successful status after restart.
    /// </summary>
    public Uri? HealthUrl { get; private set; }
    /// <summary>
    /// Gets the optional JSON endpoint that must report the expected version after restart.
    /// </summary>
    public Uri? VersionUrl { get; private set; }
    /// <summary>
    /// Gets the process exit timeout in seconds; the default is 60.
    /// </summary>
    public int WaitSeconds { get; private set; } = 60;
    /// <summary>
    /// Gets the health and version check timeout in seconds; the default is 60.
    /// </summary>
    public int StartupTimeout { get; private set; } = 60;
    /// <summary>
    /// Gets a value indicating whether release listing is requested.
    /// </summary>
    public bool ListReleases { get; private set; }
    /// <summary>List available manufacturer settings from the DeviceProfiles catalog index.</summary>
    public bool ListManufacturerSettings { get; private set; }
    /// <summary>HTTPS manufacturer index used for settings listing.</summary>
    public Uri CatalogUrl { get; private set; } = new("https://gurux.github.io/Gurux.DLMS.DeviceProfiles/manufacturers.json");
    /// <summary>
    /// Gets the maximum number of releases to return; the default is five.
    /// </summary>
    public int Count { get; private set; } = 5;
    /// <summary>
    /// Gets a value indicating whether prereleases are included when listing releases.
    /// </summary>
    public bool IncludePrereleases { get; private set; }
    /// <summary>
    /// Gets a value indicating whether check results are written as JSON and download progress is suppressed.
    /// </summary>
    public bool Json { get; private set; }
    /// <summary>
    /// Gets a value indicating whether application or service restart is disabled.
    /// </summary>
    public bool NoRestart { get; private set; }
    /// <summary>
    /// Gets a value indicating whether command-line help should be displayed.
    /// </summary>
    public bool ShowHelp { get; private set; }

    /// <summary>
    /// Parses command-line arguments and validates required options and batch-check constraints.
    /// </summary>
    public static GXUpdaterOptions Parse(string[] args)
    {
        GXUpdaterOptions result = new();
        bool countSpecified = false;
        bool prereleaseSpecified = false;

        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            result.ShowHelp = true;
            return result;
        }
        result.Command = args[0].ToLowerInvariant();
        for (int pos = 1; pos < args.Length; ++pos)
        {
            string key = args[pos];
            string Next()
            {
                if (++pos >= args.Length)
                {
                    throw new ArgumentException($"Missing value for {key}.");
                }
                return args[pos];
            }
            switch (key)
            {
                case "--application": result.Application = Next(); break;
                case "--targets": result.Targets = Next(); break;
                case "--max-concurrency": result.MaxConcurrency = int.Parse(Next()); break;
                case "--repository": result.Repository = Next(); break;
                case "--asset": result.AssetPattern = Next(); break;
                case "--token": result.Token = Next(); break;
                case "--sha256": result.Sha256 = Next(); break;
                case "--process-id": result.ProcessId = int.Parse(Next()); break;
                case "--service": result.Service = Next(); break;
                case "--health-url": result.HealthUrl = new Uri(Next()); break;
                case "--version-url": result.VersionUrl = new Uri(Next()); break;
                case "--wait-seconds": result.WaitSeconds = int.Parse(Next()); break;
                case "--startup-timeout": result.StartupTimeout = int.Parse(Next()); break;
                case "--list-manufacturer-settings": result.ListManufacturerSettings = true; break;
                case "--catalog-url": result.CatalogUrl = new Uri(Next(), UriKind.Absolute); break;
                case "--list-releases": result.ListReleases = true; break;
                case "--count":
                    result.Count = int.Parse(Next());
                    countSpecified = true;
                    break;
                case "--prerelease":
                    result.IncludePrereleases = true;
                    prereleaseSpecified = true;
                    break;
                case "--json": result.Json = true; break;
                case "--no-restart": result.NoRestart = true; break;
                default: throw new ArgumentException($"Unknown option '{key}'.");
            }
        }

        if (result.Command != "check" && result.Command != "update")
        {
            throw new ArgumentException($"Unknown command '{result.Command}'.");
        }
        if (result.ListManufacturerSettings)
        {
            if (result.Command != "check" || result.ListReleases || result.Targets != null || result.Application != null ||
                !string.IsNullOrEmpty(result.Repository) || countSpecified || prereleaseSpecified || result.AssetPattern != null)
                throw new ArgumentException("--list-manufacturer-settings requires check and cannot be combined with application, targets or release options.");
            if (result.CatalogUrl.Scheme != "https")
                throw new ArgumentException("--catalog-url must be an absolute HTTPS manufacturer index URL.");
            return result;
        }
        if (result.Command == "update" && result.ListReleases)
        {
            throw new ArgumentException("--list-releases is supported by the check command only.");
        }
        if (result.Command == "update" && countSpecified)
        {
            throw new ArgumentException("--count is supported by --list-releases only.");
        }
        if (result.Command == "update" && prereleaseSpecified)
        {
            throw new ArgumentException("--prerelease is supported by --list-releases only.");
        }
        if (!result.ListReleases && countSpecified)
        {
            throw new ArgumentException("--count is supported by --list-releases only.");
        }
        if (!result.ListReleases && prereleaseSpecified)
        {
            throw new ArgumentException("--prerelease is supported by --list-releases only.");
        }
        if (result.ListReleases && !string.IsNullOrWhiteSpace(result.Targets))
        {
            throw new ArgumentException("--targets is not supported by --list-releases.");
        }
        if (result.Command == "update" && !string.IsNullOrWhiteSpace(result.Targets))
        {
            throw new ArgumentException("--targets is supported by the check command. Update targets individually.");
        }
        if (result.MaxConcurrency < 1)
        {
            throw new ArgumentException("--max-concurrency must be greater than zero.");
        }
        if (result.Count < 1)
        {
            throw new ArgumentException("--count must be greater than zero.");
        }

        if (result.ListReleases)
        {
            if (string.IsNullOrWhiteSpace(result.Repository) || !result.Repository.Contains('/'))
            {
                throw new ArgumentException("--repository owner/name is required.");
            }
            return result;
        }

        if (result.Command == "update" || string.IsNullOrWhiteSpace(result.Targets))
        {
            if (string.IsNullOrWhiteSpace(result.Application))
            {
                throw new ArgumentException("--application is required.");
            }
            if (string.IsNullOrWhiteSpace(result.Repository) || !result.Repository.Contains('/'))
            {
                throw new ArgumentException("--repository owner/name is required.");
            }
        }
        return result;
    }

    /// <summary>
    /// Contains command-line usage, options, and exit code descriptions.
    /// </summary>
    public const string HelpText = """
Gurux.Updater (.NET 10)

Usage:
  Gurux.Updater check  --application <path> --repository <owner/name> [options]
  Gurux.Updater check  --repository <owner/name> --list-releases [options]
  Gurux.Updater check  --list-manufacturer-settings [--catalog-url <url>] [--json]
  Gurux.Updater check  --targets <targets.json> [options]
  Gurux.Updater update --application <path> --repository <owner/name> [options]

Options:
  --targets <file>           Check multiple applications/add-ins from a JSON file.
  --max-concurrency <n>      Maximum parallel checks. Default: 4.
  --asset <pattern>          Release asset pattern, e.g. *win-x64*.zip.
  --list-manufacturer-settings List available profiles (no profile downloads).
  --catalog-url <url>        HTTPS manufacturer index URL.
  --list-releases            List recent releases. Default count: 5.
  --count <n>                Number of releases to list. Default: 5.
  --prerelease               Include prereleases when listing releases.
  --process-id <pid>         Wait for this process to exit before installing.
  --service <name>           Windows Service or systemd service to stop/start.
  --health-url <url>         Wait for HTTP success after restart.
  --version-url <url>        Verify the restarted application's version endpoint.
  --startup-timeout <sec>    Health/version startup timeout. Default: 60.
  --wait-seconds <n>         Maximum process wait time. Default: 60.
  --sha256 <hash>            Verify downloaded package before installation.
  --token <token>            Optional GitHub token for private repositories.
  --json                     Print check result as JSON.
  --no-restart               Do not restart the application after update.
  --help                     Show help.

Exit codes:
  0   Success / no update.
  10  Update is available (check command).
  20  Update available in a container; deploy a new image.
  1   Error.
""";
}
