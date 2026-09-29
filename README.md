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

## Command-line options

| Option | Meaning / default |
| --- | --- |
| `--application <path>` | Application file; required for single-target checks and updates. |
| `--repository <owner/name>` | GitHub repository; required with `--application`. |
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
