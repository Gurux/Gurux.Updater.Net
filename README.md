# Gurux.Updater.Net

A .NET 10 library and command-line tool for checking GitHub releases and updating
applications and add-ins. The library provides update discovery, asset downloads,
progress reporting, HTTP health checks, and Windows/systemd service management.
The separate CLI installs ZIP packages and can run independently of the application
being updated.

## Projects

| Project | Purpose | Package ID |
| --- | --- | --- |
| `Gurux.Updater.Net` | Reusable library; primary namespace `Gurux.Updater` | `Gurux.Updater` |
| `Gurux.Updater` | CLI executable (`Gurux.Updater`) | `Gurux.Updater.Tool` |

## Install the command-line tool

Install the published `Gurux.Updater.Tool` package globally:

```shell
dotnet tool install --global Gurux.Updater.Tool
```

Run the installed tool:

```shell
gurux-updater --help
```

## Build and pack from source

Build from the repository root with the .NET 10 SDK:

```shell
dotnet build Gurux.Updater.slnx -c Release
dotnet run --project Gurux.Updater -- --help
```

Create the `Gurux.Updater.Tool` package from the CLI project:

```shell
dotnet pack Gurux.Updater/Gurux.Updater.csproj -c Release -o artifacts
```

To install this local development package:

```shell
dotnet tool install --global Gurux.Updater.Tool --add-source ./artifacts --version 0.0.1-local
```

The CLI project defaults to version `0.0.1-local` when `BUILD_BUILDNUMBER` is not
set. Use the generated package version when installing a build with a different
version. After installation, invoke the tool as `gurux-updater`.

## Use the library

### Local development catalog

Register a deployment ZIP with the local catalog server:

```shell
dotnet Gurux.Updater.Tool.dll --catalog-url http://localhost:8000/catalog.json Gurux.SMTP.0.0.1-local.zip
```

An omitted command plus a ZIP argument selects `publish`. The updater reads
metadata without loading package code: AMI modules use their declared module ID
(SMTP is `Smtp`), applications use their main assembly name, and releases use
assembly informational version or deployment version metadata. A `-local` or
other prerelease version is automatically marked as a prerelease.

Supply explicit metadata when a package cannot identify itself:

```shell
dotnet Gurux.Updater.Tool.dll publish --catalog-url http://localhost:8000/catalog.json package.zip --product MyApp --type application --version 1.2.3
```

The server stores the ZIP, updates `catalog-sources.json`, and immediately serves
the release from `catalog.json`. Repeating identical product/version/package data
is idempotent. Different bytes for an existing version require a new version
number. Listing development releases requires `--prerelease`, for example:

```shell
dotnet Gurux.Updater.Tool.dll --catalog-url http://localhost:8000/catalog.json --product Smtp --prerelease
```

Update and restart `scripts/serve_catalog.py` before the first upload; older
servers only support reading files. GitHub Pages catalogs are read-only and
cannot accept this upload command.

The CLI lists catalog products and releases without requiring an application or
GitHub repository:

```shell
Gurux.Updater.Tool.exe --catalog-url http://localhost:8080
Gurux.Updater.Tool.exe check --catalog-url http://localhost:8080/catalog.json --prerelease --json
```

Omitting the command selects `check`. A server root URL resolves to `/catalog.json`.
`--count` limits releases per product (default one); `--prerelease` includes test
releases. This lists available releases without downloading or installing packages.

The default catalog is `http://localhost:8000/catalog.json`. List all modules,
all applications, or the versions of a specific product by its catalog ID:

```shell
Gurux.Updater.Tool.exe --list-modules
Gurux.Updater.Tool.exe --list-applications
Gurux.Updater.Tool.exe --list-modules --list-applications
Gurux.Updater.Tool.exe --product Gurux.DLMS.AMI --list-releases
Gurux.Updater.Tool.exe --product Gurux.DLMS.AMI --count 10 --prerelease --json
```

By default, only the latest stable release is shown. `--prerelease` allows the
latest release to be a prerelease; `--count` can include older releases as well.
Use `--catalog-url` to override the catalog address. Manufacturer settings keep
their separate public index default with `--list-manufacturer-settings`.
With that option, `--catalog-url` also accepts an update catalog containing
`Gurux.DLMS.DeviceProfiles`; the tool follows the latest stable release's
`manufacturers.json` asset to list the settings.

`GXCatalogUpdateService` can read catalogs served by the local catalog project:

```csharp
using Gurux.Updater.Services;

using var http = new HttpClient();
var service = new GXCatalogUpdateService(http);
var catalog = await service.GetCatalogAsync(new Uri("http://localhost:8000/catalog.json"));
var releases = await service.GetReleasesAsync(catalog, "Gurux.AMI.TestModule", "*.zip");
```

Start the server from `C:\Projects\Gurux.AMI.Catalog` with
`python scripts/serve_catalog.py --port 8000`. Use `--sources catalog-sources.local.json`
when that file registers your local ZIP packages. Local catalog package URLs may
use HTTP or HTTPS for catalogs and remote package URLs. `IsSupportedUri` exposes this URL rule to hosts.

AMI's `Updater:CatalogUrl` setting selects the catalog. Its Development settings
use `http://localhost:8000/catalog.json`; other environments retain the published
HTTPS catalog. Restart AMI after changing this setting.

### GitHub releases

Reference `Gurux.Updater.Net/Gurux.Updater.Net.csproj` from a project in this
solution, or reference the `Gurux.Updater.Net` package from your package source.
Configure the supplied `HttpClient` with a GitHub User-Agent header:

```csharp
using Gurux.Updater;
using Gurux.Updater.Enums;

using HttpClient client = new();
client.DefaultRequestHeaders.UserAgent.ParseAdd("MyApplication/1.0");
GXGitHubUpdateService updater = new(client);

GXUpdateTarget target = new()
{
    Name = "Gurux.Data.Relay",
    Type = UpdateTargetType.Application,
    Application = "Gurux.Data.Relay.dll",
    Repository = "Gurux/Gurux.Data.Relay",
    AssetPattern = "*win-x64*.zip"
};

GXUpdateInfo update = await updater.CheckAsync(target, CancellationToken.None);
if (update.UpdateAvailable && update.Asset is not null)
{
    IProgress<GXUpdateProgress> progress = new Progress<GXUpdateProgress>(value =>
        Console.WriteLine($"Downloaded {value.BytesReceived} bytes"));
    await updater.DownloadAsync(
        update.Asset.DownloadUrl, "update.zip", progress, CancellationToken.None);
}
```

Set `CurrentVersion` to check a known version without reading an application file.
It takes precedence over `Application`. Otherwise, the library reads the assembly
version, falling back to file version metadata for non-managed binaries.

For multiple targets, call `CheckAsync(targets, maxConcurrency, cancellationToken)`.
Results retain input order and contain either `Update` information or an `Error`;
`Succeeded` indicates whether the check succeeded. Cancellation propagates to the
caller. Batch targets require a name, an application path or current version, and
a repository in `owner/name` format.

The library downloads files; the CLI implements installation and rollback.

## Check for updates

```shell
gurux-updater check --application Gurux.Data.Relay.dll --repository Gurux/Gurux.Data.Relay
gurux-updater check --application Gurux.Data.Relay.dll --repository Gurux/Gurux.Data.Relay --json
```

Without installing the tool, use `dotnet run --project Gurux.Updater --` in place
of `gurux-updater` in these commands.

### Batch and add-in checks

Create a `targets.json` file:

```json
[
  {
    "name": "Data Relay",
    "type": 0,
    "application": "./Gurux.Data.Relay.dll",
    "repository": "Gurux/Gurux.Data.Relay",
    "assetPattern": "*win-x64*.zip"
  },
  {
    "name": "Example add-in",
    "type": 1,
    "currentVersion": "1.0.0",
    "repository": "your-organization/your-add-in"
  }
]
```

Replace the example paths and repositories with your own. `type` is numeric:
`0` means application and `1` means add-in (the default). Relative application
paths are resolved from the working directory, not the targets file directory.

```shell
gurux-updater check --targets targets.json --max-concurrency 4 --json
```

Batch mode supports discovery only. Install updates one target at a time.

## Install an update

For a Linux systemd service:

```shell
gurux-updater update --application /opt/gurux/data-relay/Gurux.Data.Relay.Web.Server.dll --repository Gurux/Gurux.Data.Relay --asset "*linux-x64*.zip" --service gurux-data-relay --health-url http://localhost:5000/health
```

Use `--service` with a Windows service name to manage a Windows service. Service
operations require permission to control the service, and installation requires
write access to the application directory and its sibling backup directory.

For a standalone application, `--process-id <pid>` waits for the existing process
to exit; it does not stop the process. Without `--service` or `--process-id`, the
CLI does not coordinate shutdown. Keep the updater outside the directory it updates.
Standalone restart launches the application path through the operating system
shell, so use a directly launchable executable for reliable automatic restart.

The installation workflow:

1. Check for a newer release and select a downloadable asset.
2. Download and extract the ZIP, optionally verifying `--sha256` first.
3. Stop the service or wait for the specified process to exit.
4. Copy the application directory to a sibling directory with a `.backup` suffix,
   replacing any previous backup.
5. Copy extracted files over the application directory and optionally restart.
6. Check the configured health/version endpoints. If copying, restart, or these
   checks fail, attempt to restore the backup and restart the service when enabled.

ZIP contents must match the application directory layout: put application files
at the archive root, without an extra enclosing directory. Installation overwrites
matching files and retains existing files that are absent from the package.
The backup remains after a successful update.

`--health-url` requires an HTTP success response. `--version-url` expects JSON such
as `{"version":"1.2.3"}`. Version endpoint comparison ignores leading `v`/`V` and
build metadata after `+`. With no endpoints configured, restart is not checked.
`--no-restart` skips restart and endpoint checks; a stopped service remains stopped.

When a container is detected through `DOTNET_RUNNING_IN_CONTAINER=true` or
`/.dockerenv`, the update command reports an available update and exits with code
`20` without installing it. Deploy a new container image instead.

## Install a local package for testing

Install or update an application or module from a local ZIP without GitHub access
or a version comparison:

```shell
gurux-updater update --local ./packages/MyApp.zip --destination ./test --no-restart
```

The destination directory is created if missing. Existing installations are backed
up to `<destination>.backup` before files are overwritten; installation failures
restore the backup. Files absent from the ZIP are retained. ZIP entries are relative
to the destination directory, without an enclosing package directory. The local
package is installed regardless of its version, including older test builds.

`--no-restart` needs only the destination directory, making it suitable for modules.
To restart an application, omit `--no-restart` and supply `--application` with the
application file path inside the destination, or supply `--service` for a service.
The specified application must be included in the ZIP. Process waiting, service
control, `--sha256`, and `--health-url` work as with downloaded updates.
`--version-url` is unavailable because local ZIPs have no release version metadata.
Local installation is explicitly allowed in containers for testing.

From this source checkout:

```shell
dotnet run --project Development/Gurux.Updater.Tool -- update --local ./packages/MyApp.zip --destination ./test --no-restart
```

## Command-line options

| Option | Meaning / default |
| --- | --- |
| `--application <path>` | Application file; required for GitHub single-target checks and updates, optional for local installation. |
| `--repository <owner/name>` | GitHub repository; required for GitHub checks and updates. |
| `--local <zip>` | Install a local ZIP without release discovery or version comparison. |
| `--destination <directory>` | Target directory; required with `--local`. |
| `--targets <file>` | JSON target list for the `check` command only. |
| `--max-concurrency <n>` | Positive number of concurrent batch checks; default `4`. |
| `--asset <pattern>` | Case-insensitive asset name pattern, for example `*win-x64*.zip`. |
| `--token <token>` | Optional GitHub bearer token, including for release discovery in private repositories. |
| `--sha256 <hash>` | Expected hexadecimal package SHA-256; comparison ignores case and hyphens. |
| `--process-id <pid>` | Process to wait for before installation. |
| `--wait-seconds <n>` | Process exit timeout in seconds; default `60`. |
| `--service <name>` | Windows or systemd service to stop/start; takes precedence over `--process-id`. |
| `--health-url <url>` | HTTP endpoint to check after restart. |
| `--version-url <url>` | JSON version endpoint to check after restart. |
| `--startup-timeout <sec>` | Health/version polling timeout; default `60` seconds. |
| `--json` | JSON output for checks; suppresses download progress for updates, whose messages remain text. |
| `--no-restart` | Skip restart and post-restart endpoint checks. |
| `--help`, `-h` | Show usage and exit. |

## Release and asset selection

The updater queries the repository's latest GitHub release. Release tags are
trimmed of whitespace and leading `v`/`V`; the suffix beginning at the first hyphen
is removed. Numeric versions use `System.Version` comparison. If numeric parsing
fails, comparison falls back to case-insensitive text comparison. This is not a
full Semantic Versioning implementation.

With an asset pattern, the first asset containing the asterisk-separated parts
in order is selected. Matching ignores case and does not anchor the start or end
of the name. Only `*` separates pattern parts; other characters are literal.
For installation, specify a pattern that selects a ZIP package.

Without a pattern, selection prefers a ZIP whose name contains the operating
system (`win`, `linux`, or `osx`) and OS architecture. It recognizes `amd64` as an
alias for `x64` and `aarch64` for `arm64`, then falls back to the first ZIP asset.
Use an explicit pattern when a release contains multiple packages.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Successful update, no update available, or help displayed. |
| `10` | The `check` command found an update. |
| `20` | The `update` command found an update inside a detected container; installation was skipped. |
| `1` | Error. |

For batch checks, an available update takes precedence over failed checks in the
exit code: inspect individual results for errors even when the command returns `10`.

## Localization catalogs and complete JSON language packages

All CLI catalog operations use HTTP or HTTPS through `--catalog-url`.
The default is `http://localhost:8000/catalog.json`. Local `--catalog` paths and
`localization add/update` commands are no longer supported.

Publish a new owner or a new localization version from a complete JSON package:

```powershell
dotnet Gurux.Updater.Tool.dll publish ./Gurux.AMI.SMTP-1.json --catalog-url http://localhost:8000/catalog.json
```

The JSON `owner`, `ownerType`, and `version` fields define its identity.
Increase `version` when changing published resources. Uploading identical bytes
for an existing version succeeds; different bytes for the same version conflict.
The server must support POST at the localization index URL. Restart
`Gurux.AMI.Catalog/scripts/serve_catalog.py` after updating the server script.
The shared update catalog must register `Gurux.AMI.Localization` with a
`localization-index.json` asset pointing to the server's localization index.

```powershell
dotnet Gurux.Updater.Tool.dll check --list-localizations --catalog-url http://localhost:8000/catalog.json
dotnet Gurux.Updater.Tool.dll update --localization ./smtp-localization.json --catalog-url http://localhost:8000/catalog.json
dotnet Gurux.Updater.Tool.dll update --localization ./localizations.zip --catalog-url http://localhost:8000/catalog.json
```

`update --localization` downloads a newer published package into the supplied
local JSON or ZIP. It compares package versions, not modified content, and never
publishes missing owners. Use `publish` first if an owner is missing. ZIP entries
are updated independently; paths and non-JSON contents are preserved. Downloads
validate size, SHA-256, JSON and owner metadata before replacing local files.

### Package format

A package contains every supported language for one application or module:

```json
{
  "schemaVersion": 1,
  "owner": "Gurux.SMTP",
  "ownerType": "module",
  "version": "1.0.0",
  "ownerVersionRange": "[1.0.0,2.0.0)",
  "defaultCulture": "en",
  "description": "SMTP module translations",
  "releaseNotes": "Initial English and Finnish translations",
  "resources": {
    "en": { "Title": "Mail", "Send": "Send" },
    "fi": { "Title": "Posti", "Send": "Lähetä" }
  }
}
```

`ownerType` is `application` or `module`. `ownerVersionRange`, `description`
and `releaseNotes` are optional. Supported cultures are the keys of `resources`;
`defaultCulture` must be included. Identity is `owner + culture + key`, and
resource keys are case-sensitive: `Title` and `title` are distinct. Duplicate JSON
properties, including repeated resource keys, are rejected before deserialization.
Values must be strings; an empty translation is allowed.

Package versions and owner compatibility ranges use
[NuGet.Versioning](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning).
Owner identifiers must be safe filenames (letters, digits, dots, underscores,
hyphens); Windows reserved filenames and trailing dots are rejected. Owners
differing only in case cannot coexist in a checkout.

A ready-to-use example is in `examples/localization/smtp-localization.json`
at the repository root.

### List, download and validate through the server

```powershell
gurux-updater localization list --catalog-url http://localhost:8000/catalog.json --all-versions --include-prereleases
gurux-updater localization list --catalog-url https://example.com/catalog.json --owner Smtp --culture fi --json
gurux-updater localization download --catalog-url http://localhost:8000/catalog.json --owner Smtp --output ./downloads
gurux-updater localization validate --catalog-url http://localhost:8000/catalog.json
```

Package references resolve relative to the remote localization index URL.
Local JSON input and download output files remain local; catalogs are remote.

## Development server diagnostics

Start from the catalog project root with `./scripts/start_catalog.ps1 -Port 8000`.
The launcher builds Gurux.Updater.Tool and starts the single `scripts/serve_catalog.py`.
`GET /health` returns server version, script path, process ID and start time.
Stop with Ctrl+C. Python and .NET 10 are required.

Localization uploads use the updater's `localization validate-package --package <json>`
command for identical server/client validation, including duplicate properties, culture
aliases, resource values and NuGet compatibility ranges. Valid input returns JSON with
`valid: true`; invalid packages return `valid: false` and exit code 2. Validator execution
failures return HTTP 503; invalid input returns HTTP 400 without writing.
For direct Python startup outside the sibling checkout, set GURUX_LOCALIZATION_VALIDATOR
to a current Gurux.Updater.Tool.dll. Validation times out after 30 seconds.
Identical uploads print Already published; changed same-version content requests a version
increase. Local update status includes local and catalog versions.
Storage remains under _site/localization.
