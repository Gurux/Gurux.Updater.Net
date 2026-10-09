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
using Gurux.Updater.Services;
using Gurux.Updater.Enums;

namespace Gurux.Updater.Tool;

/// <summary>
/// Contains parsed command-line options for checking and installing updates.
/// </summary>
public sealed class GXUpdaterOptions
{
    /// <summary>
    /// Gets the command to execute: check, update, or publish.
    /// </summary>
    public string Command { get; private set; } = "check";
    /// <summary>
    /// Gets the path to the application or add-in whose version is checked.
    /// </summary>
    public string? Application { get; private set; }
    /// <summary>Gets the local ZIP package to install without release discovery.</summary>
    public string? LocalPackage { get; private set; }
    /// <summary>Gets the destination directory for a local installation.</summary>
    public string? Destination { get; private set; }
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
    /// <summary>List localization packages from the selected catalog.</summary>
    public bool ListLocalization { get; private set; }
    /// <summary>Existing JSON package or ZIP containing JSON packages to update in place.</summary>
    public string? Localization { get; private set; }
    /// <summary>List products and releases from the selected update catalog.</summary>
    public bool ListCatalog { get; private set; }
    /// <summary>List every module registered in the update catalog.</summary>
    public bool ListModules { get; private set; }
    /// <summary>List every application registered in the update catalog.</summary>
    public bool ListApplications { get; private set; }
    /// <summary>List every agent registered in the update catalog.</summary>
    public bool ListAgents { get; private set; }
    /// <summary>Catalog product ID whose releases should be listed.</summary>
    public string? Product { get; private set; }
    /// <summary>Deployment ZIP to register with a writable catalog server.</summary>
    public string? PublishPackage { get; private set; }
    /// <summary>Optional explicit catalog product type for publishing.</summary>
    public GXCatalogProductType? PackageType { get; private set; }
    /// <summary>Optional explicit release version for publishing.</summary>
    public string? PackageVersion { get; private set; }
    /// <summary>Update catalog URL, or manufacturer index URL with --list-manufacturer-settings.</summary>
    public Uri CatalogUrl { get; private set; } = new("http://localhost:8000/catalog.json");
    /// <summary>
    /// Gets the maximum number of releases to return; the default is one.
    /// </summary>
    public int Count { get; private set; } = 1;
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
        bool catalogSpecified = false;

        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            result.ShowHelp = true;
            return result;
        }
        int firstOption = 0;
        bool explicitCommand = !args[0].StartsWith('-') && !args[0].EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        if (explicitCommand)
        {
            result.Command = args[0].ToLowerInvariant();
            firstOption = 1;
        }
        for (int pos = firstOption; pos < args.Length; ++pos)
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
                case "--application":
                    result.Application = Next();
                    break;
                case "--local":
                    result.LocalPackage = Next();
                    break;
                case "--destination":
                    result.Destination = Next();
                    break;
                case "--targets":
                    result.Targets = Next();
                    break;
                case "--max-concurrency":
                    result.MaxConcurrency = int.Parse(Next());
                    break;
                case "--repository":
                    result.Repository = Next();
                    break;
                case "--asset":
                    result.AssetPattern = Next();
                    break;
                case "--token":
                    result.Token = Next();
                    break;
                case "--sha256":
                    result.Sha256 = Next();
                    break;
                case "--process-id":
                    result.ProcessId = int.Parse(Next());
                    break;
                case "--service":
                    result.Service = Next();
                    break;
                case "--health-url":
                    result.HealthUrl = new Uri(Next());
                    break;
                case "--version-url":
                    result.VersionUrl = new Uri(Next());
                    break;
                case "--wait-seconds":
                    result.WaitSeconds = int.Parse(Next());
                    break;
                case "--startup-timeout":
                    result.StartupTimeout = int.Parse(Next());
                    break;
                case "--list-manufacturer-settings":
                    result.ListManufacturerSettings = true;
                    break;
                case "--list-localizations":
                    result.ListLocalization = true;
                    break;
                case "--localization":
                    result.Localization = Next();
                    break;
                case "--list-modules":
                    result.ListModules = true;
                    break;
                case "--list-agents":
                    result.ListAgents = true;
                    break;
                case "--list-applications":
                    result.ListApplications = true;
                    break;
                case "--product":
                    result.Product = Next();
                    break;
                case "--type":
                    result.PackageType = Next().ToLowerInvariant() switch
                    {
                        "module" => GXCatalogProductType.Module,
                        "application" => GXCatalogProductType.Application,
                        "agent" => GXCatalogProductType.Agent,
                        _ => throw new ArgumentException("--type must be module, application or agent.")
                    };
                    break;
                case "--version":
                    result.PackageVersion = Next();
                    break;
                case "--catalog-url":
                    result.CatalogUrl = new Uri(Next(), UriKind.Absolute);
                    catalogSpecified = true;
                    break;
                case "--list-releases":
                    result.ListReleases = true;
                    break;
                case "--count":
                    result.Count = int.Parse(Next());
                    countSpecified = true;
                    break;
                case "--prerelease":
                    result.IncludePrereleases = true;
                    prereleaseSpecified = true;
                    break;
                case "--json":
                    result.Json = true;
                    break;
                case "--no-restart":
                    result.NoRestart = true;
                    break;
                default:
                    if (!key.StartsWith('-') && (key.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || key.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (result.PublishPackage != null)
                        {
                            throw new ArgumentException("Specify exactly one ZIP or localization JSON package to publish.");
                        }

                        result.PublishPackage = key;
                    }
                    else
                    {
                        throw new ArgumentException($"Unknown option '{key}'.");
                    }

                    break;
            }
        }

        if (result.PublishPackage != null && !explicitCommand)
        {
            result.Command = "publish";
        }

        if (result.ListLocalization || result.Localization != null)
        {
            if (result.ListLocalization && (result.Command != "check" || result.Localization != null) ||
                result.Localization != null && (result.Command != "update" || string.IsNullOrWhiteSpace(result.Localization)) ||
                result.PublishPackage != null || result.LocalPackage != null || result.Destination != null ||
                result.Application != null || result.Targets != null || !string.IsNullOrEmpty(result.Repository) ||
                result.ListManufacturerSettings || result.ListModules || result.ListApplications || result.ListAgents || result.ListReleases ||
                result.PackageType != null || result.PackageVersion != null || result.AssetPattern != null ||
                result.Token != null || result.ProcessId != null || result.Service != null || result.HealthUrl != null ||
                result.VersionUrl != null || result.NoRestart || countSpecified ||
                result.Localization != null && result.Product != null)
            {
                throw new ArgumentException("Use check --list-localizations or update --localization <path>, with --catalog-url, --product, --prerelease or --json. Other update/listing options cannot be combined with localization.");
            }
            if (result.Product != null && string.IsNullOrWhiteSpace(result.Product))
            {
                throw new ArgumentException("--product requires a nonempty localization owner ID.");
            }
            NormalizeCatalogUrl(result);
            return result;
        }

        if (result.Command != "check" && result.Command != "update" && result.Command != "publish")
        {
            throw new ArgumentException($"Unknown command '{result.Command}'.");
        }
        if (result.PublishPackage != null || result.Command == "publish")
        {
            if (result.Command != "publish" || result.PublishPackage == null)
            {
                throw new ArgumentException("Catalog publishing requires publish <package.zip|localization.json>.");
            }

            if (result.LocalPackage != null || result.Destination != null || result.Application != null ||
                            result.Targets != null || result.ListModules || result.ListApplications || result.ListAgents || result.ListReleases ||
                            result.ListManufacturerSettings || countSpecified || !string.IsNullOrEmpty(result.Repository) ||
                            result.Token != null || result.AssetPattern != null || result.ProcessId != null || result.Service != null ||
                            result.HealthUrl != null || result.VersionUrl != null || result.NoRestart)
            {
                throw new ArgumentException("Catalog publishing cannot be combined with listing, GitHub, installation or restart options.");
            }

            if (result.Product != null && string.IsNullOrWhiteSpace(result.Product))
            {
                throw new ArgumentException("--product requires a nonempty catalog product ID.");
            }

            if (result.PackageVersion != null && string.IsNullOrWhiteSpace(result.PackageVersion))
            {
                throw new ArgumentException("--version requires a nonempty release version.");
            }

            NormalizeCatalogUrl(result);
            return result;
        }
        if (result.PackageType != null || result.PackageVersion != null)
        {
            throw new ArgumentException("--type and --version are supported only when publishing a ZIP package.");
        }

        if (result.LocalPackage != null || result.Destination != null)
        {
            if (result.Command != "update" || string.IsNullOrWhiteSpace(result.LocalPackage) ||
                string.IsNullOrWhiteSpace(result.Destination))
            {
                throw new ArgumentException("Local installation requires update --local <zip> --destination <directory>.");
            }

            if (result.Targets != null || result.ListReleases || result.ListManufacturerSettings ||
                            countSpecified || prereleaseSpecified || !string.IsNullOrEmpty(result.Repository) ||
                            result.AssetPattern != null || result.Token != null || catalogSpecified ||
                            result.ListModules || result.ListApplications || result.ListAgents || result.Product != null)
            {
                throw new ArgumentException("Local installation cannot be combined with GitHub, catalog or batch options.");
            }

            if (!result.NoRestart && string.IsNullOrWhiteSpace(result.Service) && string.IsNullOrWhiteSpace(result.Application))
            {
                throw new ArgumentException("Use --no-restart or provide --application or --service for local installation.");
            }

            if (result.VersionUrl != null)
            {
                throw new ArgumentException("--version-url is unavailable for local packages without release version metadata. Use --health-url.");
            }

            return result;
        }
        if (result.ListManufacturerSettings)
        {
            if (result.Command != "check" || result.ListReleases || result.Targets != null || result.Application != null ||
                !string.IsNullOrEmpty(result.Repository) || countSpecified || prereleaseSpecified || result.AssetPattern != null ||
                result.ListModules || result.ListApplications || result.ListAgents || result.Product != null)
            {
                throw new ArgumentException("--list-manufacturer-settings requires check and cannot be combined with application, targets or release options.");
            }

            if (!catalogSpecified)
            {
                result.CatalogUrl = new Uri("https://gurux.github.io/Gurux.DLMS.DeviceProfiles/manufacturers.json");
            }

            if (!GXCatalogUpdateService.IsSupportedUri(result.CatalogUrl))
            {
                throw new ArgumentException("--catalog-url must be an absolute HTTPS or HTTP manufacturer index URL.");
            }

            return result;
        }
        if (catalogSpecified || result.ListModules || result.ListApplications || result.ListAgents || result.Product != null)
        {
            if (result.Command != "check" || result.Targets != null || result.Application != null ||
                !string.IsNullOrEmpty(result.Repository) || result.Token != null)
            {
                throw new ArgumentException("Catalog listing requires check and cannot be combined with application, repository, token or targets options.");
            }

            NormalizeCatalogUrl(result);
            if (result.Count < 1)
            {
                throw new ArgumentException("--count must be greater than zero.");
            }

            if (result.Product != null && string.IsNullOrWhiteSpace(result.Product))
            {
                throw new ArgumentException("--product requires a nonempty catalog product ID.");
            }

            result.ListCatalog = true;
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

    private static void NormalizeCatalogUrl(GXUpdaterOptions options)
    {
        if (!GXCatalogUpdateService.IsSupportedUri(options.CatalogUrl))
        {
            throw new ArgumentException("--catalog-url must be an absolute HTTPS or HTTP update catalog URL.");
        }

        if (options.CatalogUrl.AbsolutePath == "/")
        {
            options.CatalogUrl = new UriBuilder(options.CatalogUrl) { Path = "/catalog.json" }.Uri;
        }
    }

    /// <summary>
    /// Contains command-line usage, options, and exit code descriptions.
    /// </summary>
    public const string HelpText = """
Gurux.Updater (.NET 10)

Usage:
  Gurux.Updater localization <list|add|update|download|validate> [options]
  Gurux.Updater localization --help
  Gurux.Updater [publish] [--catalog-url <url>] <package.zip> [--product <id>] [--type module|application|agent] [--version <version>]
  Gurux.Updater publish <localization.json> [--catalog-url <url>] [--json]
  Gurux.Updater [check] --catalog-url <url> [--count <n>] [--prerelease] [--json]
  Gurux.Updater [check] --list-modules [--list-applications] [catalog options]
  Gurux.Updater [check] --list-applications [catalog options]
  Gurux.Updater [check] --list-agents [catalog options]
  Gurux.Updater [check] --product <id> [--count <n>] [--prerelease] [--json]
  Gurux.Updater check  --application <path> --repository <owner/name> [options]
  Gurux.Updater check  --repository <owner/name> --list-releases [options]
  Gurux.Updater check  --list-manufacturer-settings [--catalog-url <url>] [--json]
  Gurux.Updater check  --list-localizations [--catalog-url <url>] [--product <id>] [--json]
  Gurux.Updater update --localization <json-or-zip-file> [--catalog-url <url>] [--prerelease] [--json]
  Gurux.Updater check  --targets <targets.json> [options]
  Gurux.Updater update --application <path> --repository <owner/name> [options]
  Gurux.Updater update --local <package.zip> --destination <directory> --no-restart [options]

Options:
  --local <zip>              Install a local ZIP without GitHub or version checks.
  --destination <directory> Destination directory for local installation.
  --targets <file>           Check multiple applications/add-ins from a JSON file.
  --max-concurrency <n>      Maximum parallel checks. Default: 4.
  --asset <pattern>          Release asset pattern, e.g. *win-x64*.zip.
  --list-manufacturer-settings List available profiles (no profile downloads).
  --list-localizations       List the latest localization packages from the catalog.
  --localization <json-or-zip-file> Update JSON packages using their owners and versions.
  --list-agents              List all catalog agents (combine with --list-modules or --list-applications).
  --list-modules             List all catalog modules.
  --list-applications        List all catalog applications (combine with --list-modules).
  --product <id>             List versions of one catalog product, e.g. Gurux.DLMS.AMI.
                            When publishing, overrides the ID read from the package.
  --type module|application|agent  Override the product type when publishing a ZIP.
  --version <version>        Override the release version when publishing a ZIP.
  --catalog-url <url>        List an update catalog (server root resolves to /catalog.json).
                            With --list-manufacturer-settings, selects an index or update catalog.
                            With localization, selects a catalog containing Gurux.AMI.Localization.
                            Default catalog: http://localhost:8000/catalog.json.
  --list-releases            List recent releases. Default count: 1.
  --count <n>                Maximum releases per product. Default: 1 (latest release).
  --prerelease               Include prereleases when listing releases.
                            When publishing, marks the new release as a prerelease.
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
