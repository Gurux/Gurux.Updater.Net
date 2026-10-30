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

Use `--type agent` to publish an agent deployment ZIP:

```shell
dotnet Gurux.Updater.Tool.dll publish Gurux.AMI.Agent-Gurux.DLMS.AMI.Agent-1.0.0-rc1.zip --type agent --catalog-url http://localhost:8000/catalog.json
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
all applications, all agents, or the versions of a specific product by its catalog ID:

```shell
Gurux.Updater.Tool.exe --list-modules
Gurux.Updater.Tool.exe --list-applications
Gurux.Updater.Tool.exe --list-agents
Gurux.Updater.Tool.exe --list-modules --list-applications --list-agents
dotnet Gurux.Updater.Tool.dll check --list-agents --catalog-url http://localhost:8000/catalog.json --prerelease
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

See [update status and release classification](Update-classification.md) for the
separate status, priority and security fields, catalog checks and error handling.

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

## Independent JSON localization packages

Translation sources are flat culture-named files (`en.json`, `fi.json`, `fi-FI.json`,
`sv-SE.json`) in explicitly registered directories. Values must be strings; empty
translations are allowed. Keys are case-sensitive and values are never trimmed.
No hand-maintained `localization.json` is required or read for source metadata.
Only `*.json` immediately inside the registered directory is considered; use a
translation-only directory, not a general JSON assets directory.

The commands below run from `Development/` (prepend `Development/` to project
paths when running from the repository root). Build once:

```powershell
dotnet build Gurux.Updater.Tool -c Release
$tool = './Gurux.Updater.Tool/bin/Release/net10.0/Gurux.Updater.Tool.dll'
dotnet $tool localization validate-source --source ./examples/localization/SMTP/localization --owner Gurux.AMI.SMTP --owner-type module --default-culture en --owner-version-range '[1.0.0,2.0.0)'
# Read-only plan: does not create packages, directories, lock files or timestamps.
dotnet $tool localization plan --sources ./examples/localization/sources.json --index ../../Gurux.AMI.Catalog/localization/localization-index.json --json
# Local checkout publication: persists history, does not deploy or commit automatically.
dotnet $tool localization publish-sources --sources ./examples/localization/sources.json --index ../../Gurux.AMI.Catalog/localization/localization-index.json --json
dotnet $tool localization validate-store --index ../../Gurux.AMI.Catalog/localization/localization-index.json
```

The example registers both a module and `GXDLMSDirector`, with regional cultures.
Replace its directories and owners before publishing real translations. Configuration
is `{ "sources": [...] }`; each source uses `directory`, optional `project` or `metadataPackage`, `owner`,
`ownerType`, `defaultCulture` and optional `ownerVersionRange`. Relative source and
project paths resolve beside that configuration file. Use `--source` and the metadata
flags above for a single directory, or `--sources` for an atomic multi-owner batch.
`--metadata-package owner.nupkg` can infer the NuGet `id`, optional `language` and
an explicit application/module package type from its nuspec. A deployment ZIP uses
the existing package metadata reader. Provide missing type/default culture explicitly;
package versions never determine language versions. Do not combine project/package inference.

With `--project Example.csproj`, or one unambiguous nearest ancestor project, the
publisher evaluates MSBuild metadata (`PackageId`, falling back to `AssemblyName`;
`LocalizationOwnerType`; `LocalizationDefaultCulture`, falling back to `NeutralLanguage`;
optional `LocalizationOwnerVersionRange`). Executable `OutputType` infers
`application`. A library is not automatically assumed to be an AMI module: set
`LocalizationOwnerType` or `--owner-type`. Missing/ambiguous required metadata fails
clearly; command-line/configuration fields override inference. The owner binary version
never determines the translation version. These additional project properties are
optional conveniences; explicit publication configuration also works for non-.NET apps.

### Package and index format

Each immutable schema 2 package contains one language:

```json
{
  "schemaVersion": 2,
  "owner": "Gurux.AMI.SMTP",
  "ownerType": "module",
  "culture": "fi",
  "version": "1.0.1",
  "ownerVersionRange": "[1.0.0,2.0.0)",
  "defaultCulture": "en",
  "resources": { "Title": "Posti", "Send": "Lähetä" }
}
```

`defaultCulture` identifies the owner's default language; its resources need not
be present in a Finnish package. The library's existing `Resources` culture map API
is preserved: the JSON converter exposes the flat wire resources as
`package.Resources[package.Culture]` for schema 2.

The shared index retains `schemaVersion: 1`, `items`, and each owner's `releases`,
with additive `culture` and `contentSha256` fields for language releases:

```json
{
  "schemaVersion": 1,
  "generatedAt": "2026-10-30T00:00:00Z",
  "items": [{
    "owner": "Gurux.AMI.SMTP", "ownerType": "module",
    "releases": [{
      "culture": "fi", "cultures": ["fi"], "version": "1.0.1",
      "defaultCulture": "en", "ownerVersionRange": "[1.0.0,2.0.0)",
      "path": "packages/Gurux.AMI.SMTP/fi/1.0.1.json",
      "size": 123,
      "sha256": "<SHA-256 of exact downloaded bytes>",
      "contentSha256": "<SHA-256 of canonical content and metadata>"
    }]
  }]
}
```

Size/hash placeholders above are illustrative, not a publishable index. Every owner
retains all releases. `owner + culture` identifies a distribution; adding `version`
identifies a package; adding a resource `key` identifies a translation. Culture names
are normalized through .NET, aliases in source filenames are rejected, and paths,
owner identifiers, duplicate JSON keys, resource types and NuGet ranges are validated
by the shared C# code. Default-language missing keys produce warnings, not errors.

### Change detection, versions and transactions

Each culture starts at `1.0.0`. A changed language increments only its own latest
published NuGet patch number. Canonical SHA-256 includes ordinally sorted parsed
resource keys and exact string values, owner/type/culture/defaultCulture, normalized
compatibility range, effective description and release notes. It excludes version
and timestamps. Indentation, line endings, key order and equivalent JSON escaping
therefore do not publish. Resource additions/removals/value changes and effective
metadata changes do. An added language starts its own history. A removed source
language is reported, with published and installed languages retained; withdrawal
requires a separate deliberate operation.

Publication validates the entire batch and retained history, acquires an exclusive
cross-process index lock, replans against current history, writes immutable package
files, then atomically replaces the index. A repeated unchanged run changes no durable
index bytes or timestamps and creates no package. Files left before an interrupted
index commit are reused only if byte-identical; conflicting immutable paths fail.
Readers always see an index referencing complete packages. Keep index lock files
untracked and never delete them while publishers run. A publisher waits up to 30 seconds
for the lock. No client-suggested version is trusted for source publication.

### Local server and GitHub Pages

Start the sibling catalog with `scripts/start_catalog.ps1 -Port 8000`; this builds
the current updater. For direct Python startup, set `GURUX_LOCALIZATION_VALIDATOR`
to this build's `Gurux.Updater.Tool.dll`. Its shared update catalog must register
`Gurux.AMI.Localization` with the localization-index asset (the asset registration
name is retained for compatibility; resource owners can be any application/module).

```powershell
dotnet $tool localization publish-sources --sources ./examples/localization/sources.json --catalog-url http://localhost:8000/catalog.json --json
dotnet $tool localization plan --sources ./examples/localization/sources.json --catalog-url http://localhost:8000/catalog.json --json
dotnet $tool localization list --catalog-url http://localhost:8000/catalog.json --owner Gurux.AMI.SMTP --culture fi --json
dotnet $tool localization download --catalog-url http://localhost:8000/catalog.json --owner Gurux.AMI.SMTP --culture fi --owner-version 1.5.0 --output ./downloads
dotnet $tool localization status --catalog-url http://localhost:8000/catalog.json --package ./downloads/Gurux.AMI.SMTP-fi-1.0.0.json --owner-version 1.5.0
dotnet $tool localization update --catalog-url http://localhost:8000/catalog.json --package ./downloads/Gurux.AMI.SMTP-fi-1.0.0.json --owner-version 1.5.0 --json
dotnet $tool localization update --catalog-url http://localhost:8000/catalog.json --package ./languages.zip --owner-version 1.5.0 --json
```

The server persists publications beside `catalog-sources.json`, under
`localization/`, and mirrors complete snapshots into `_site/localization/`.
Commit that authoritative directory to retain local publications in Git and share
history with CI. The Catalog README documents exact Pages publication commands.
Pages is read-only: never POST to Pages. Instead publish with `--index` in the
catalog checkout, validate, then commit/push packages and index together; the catalog
workflow imports successfully published source Releases and commits their history
before deploying. `_site` and CI caches are never version-number authorities.

### Download, status and update

Select the newest compatible release of the same owner/culture, optionally using
`--owner-version`. Owners using language packages require `--culture` for download.
Default listing selects the latest release per culture; `--all-versions` exposes
retained history. Downloads verify byte size, byte SHA-256, schema, owner/type,
culture, version, metadata and content hash before an atomic replacement. JSON and
ZIP updates download only languages with a newer selected version. ZIP paths,
unrelated JSON and other contents remain intact; failed updates preserve the whole
original ZIP. Results identify cultures and errors, including rolled-back staging.
`GXLocalizationService.StatusAsync`, `DownloadAsync(..., culture: ...)`, and
`UpdatePackageAsync(..., ownerVersion: ..., results: ...)` expose the same behavior.

### Legacy schema 1 and migration

Existing complete schema 1 packages remain readable, downloadable and updatable;
`publish <localization.json>` and `update --localization <JSON-or-ZIP>` retain their
legacy meaning. Legacy server uploads remain idempotent for identical bytes and
reject different content for an existing version. New source publication never
replaces an existing published version. The older offline `AddAsync(..., replace: true)`
API is retained only for compatibility with offline editing, not the publication path.

A release without `culture` is a legacy all-language package. Both forms can remain
in one index, and their version numbers are never compared against each other.
After an owner adopts independent languages, default listings show those languages,
new legacy uploads are rejected, and updating a legacy installation fails with an
explicit migration instruction. Download the selected cultures into separate files,
update the application's resource loader, then retire its old all-language file.
Do not silently replace that file with one culture. Existing older updater builds
do not understand this extension: upgrade consumers before switching an owner.

Store downloaded original packages separately from application-specific user
translation overrides. The updater neither merges overrides nor accesses a user
translation database; apply fallback/overrides in the host application's resource
loader after loading the original package.

Run the tracked regression suite with `dotnet test Localization.Tests`; the solution
also includes it. The GitHub workflow runs this suite on .NET 10/Linux.

### Tag-driven production publication

Production automation now follows `changed source -> annotated tag -> GitHub Release
-> repository_dispatch -> Catalog -> Pages`. Catalog CI imports published artifacts;
it does not generate language versions directly from moving source checkouts.

Register explicit source units in `content-sources.json`:

```json
{
  "sources": [
    { "kind": "localization", "directory": "localization", "owner": "App", "ownerType": "application", "defaultCulture": "en" },
    { "kind": "resources", "directory": "resources/templates", "owner": "App", "ownerType": "application", "name": "templates" }
  ]
}
```

Tags are `localization/<owner>/<culture>/v<version>` and
`resources/<owner>/<name>/v<version>`. Each resource unit is an owner's separate named
directory (not device profiles or scripts). Only units with changed canonical content
or metadata receive a tag/version. Resource ZIPs contain exact source files plus
`resource-package.json`, use deterministic ordering/timestamps and have an independent
content SHA-256 and artifact-byte SHA-256. Paths/names must be safe on Windows/Linux.

The tag annotation stores the complete release manifest, reserving its version.
The source workflow finishes interrupted reserved tags before allocating more versions.
An incomplete tag retry rebuilds from the pinned commit and checks exact bytes.
An already published tag retry uses its existing verified assets without rebuilding.
Published assets are never overwritten. After draft creation/upload/publication the
workflow downloads and verifies the published assets before sending Catalog a
`localization-release` or `resources-release` event. Failed upload/verification never
notifies; retrying an already published tag sends the notification again without
creating another tag, version or asset.

Reusable workflow: `.github/workflows/release-content.yml`; caller example:
`Development/examples/content/publish-content.yml`. The installed Updater caller uses
`Development/content-sources.json` (initially empty) and `Development/content/**`.
Other repositories register real directories and adjust caller path filters to include
their source project/package metadata. Configure `CATALOG_DISPATCH_TOKEN` with Catalog
Contents:write, grant the caller Contents:write, and register the source repository in
Catalog's `content-release-sources.json`. The detecting job runs the Release stage
itself after pushing each tag, so GITHUB_TOKEN's suppression of tag-triggered workflows
does not interrupt the chain. Human tag pushes also work.

```powershell
# Source repository: retry only this existing tag and repeat its notification.
gh workflow run publish-content.yml --repo Owner/Source -f tag=localization/App/fi/v1.0.1
# Detect changed units, using latest retained tag manifests as history.
gh workflow run publish-content.yml --repo Owner/Source
```

Low-level C# file protocol (git/gh driver uses these):

```powershell
dotnet $tool content prepare --sources ./examples/content/content-sources.json --history ./tag-history.json --output ./release-artifacts
dotnet $tool content rebuild --sources ./examples/content/content-sources.json --manifest ./release-manifest.json --output ./retry-artifacts
dotnet $tool content verify --manifest ./release-manifest.json --package ./package-file
dotnet $tool content import --manifest ./release-manifest.json --package ./package-file --catalog-root ./catalog-checkout
dotnet $tool content validate-resources --catalog-root ./catalog-checkout
```

`tag-history.json` is an array of manifest annotations, initially `[]`. The driver
`Development/scripts/publish_content.py` fetches tag history, reserves annotated tags,
publishes immutable GitHub assets and dispatches last. Its repository-wide workflow
concurrency and non-forced tag pushes prevent competing version reservations.
Catalog verifies the manifest against the annotated Git tag and imports the exact
version into `localization/packages/...` or `resources/packages/...`; duplicate dispatch
is a no-op. Its reconciliation runs recover missed dispatches. Source tags/Releases
and Catalog's Git-tracked package/index history persist across clean CI runners.
If migrating existing catalog-only versions, align tag/Release history before enabling
automatic tag allocation; conflicting immutable versions are rejected.

Run publication orchestration tests with
`python -m unittest discover -s Development/tests-content-release -v` from repository
root, in addition to the .NET localization/resource regression suite.

## Standalone C# scripts

The updater supports `script publish`, `list`, `download`, `status`, `update` and `reject`.
Scripts are distributed as a source file plus versioned metadata; local edits can be
restored from the installed original with a backup. Scripts are never executed.
See [script catalog usage](Script-catalog.md).
