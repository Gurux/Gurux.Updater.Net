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
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Gurux.Updater.Services;

/// <summary>
/// Checks GitHub releases for updates and downloads release assets.
/// </summary>
public sealed class GXGitHubUpdateService
{
    private const int ReleasePageSize = 100;
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes the update service with the supplied HTTP client.
    /// </summary>
    public GXGitHubUpdateService(HttpClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Checks the latest GitHub release and selects an asset for the specified target.
    /// </summary>
    public async Task<GXUpdateInfo> CheckAsync(GXUpdateTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!TryGetRepositoryParts(target.Repository, out _, out _))
        {
            throw new ArgumentException("Repository owner/name is required.", nameof(target));
        }

        string current = GetCurrentVersion(target);
        GXGitHubRelease release = await GetJsonAsync<GXGitHubRelease>(
            $"https://api.github.com/repos/{target.Repository}/releases/latest",
            "GitHub returned an empty release response.",
            cancellationToken);
        string latest = NormalizeVersion(release.TagName);
        GXGitHubAsset? asset = SelectAsset(release.Assets, target.AssetPattern);
        return new GXUpdateInfo
        {
            Name = target.Name!,
            Type = target.Type,
            Application = target.Application!,
            Repository = target.Repository!,
            CurrentVersion = current,
            LatestVersion = latest,
            UpdateAvailable = CompareVersions(current, latest) < 0,
            ReleaseNotes = release.Body,
            ReleaseUrl = release.HtmlUrl,
            IsContainer = IsContainer(),
            Asset = CreateUpdateAsset(asset)
        };
    }

    /// <summary>
    /// Gets the newest published GitHub releases for the specified target without reading the installed version.
    /// </summary>
    /// <param name="target">The update target whose repository and asset pattern are used.</param>
    /// <param name="count">The maximum number of releases to return after filtering.</param>
    /// <param name="includePrereleases">True to include prereleases; false to return stable releases only.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The newest published releases after filtering drafts and optional prereleases.</returns>
    public async Task<IReadOnlyList<GXUpdateRelease>> GetReleasesAsync(
        GXUpdateTarget target,
        int count = 10,
        bool includePrereleases = false,
        CancellationToken cancellationToken = default)
    {
        ValidateReleaseQueryTarget(target, count);

        List<GXUpdateRelease> releases = [];
        int page = 1;
        string repository = target.Repository!;

        while (true)
        {
            List<GXGitHubRelease> pageReleases = await GetJsonAsync<List<GXGitHubRelease>>(
                BuildReleasesRequestUri(repository, page),
                "GitHub returned an empty releases response.",
                cancellationToken);

            if (pageReleases.Count == 0)
            {
                break;
            }

            foreach (GXGitHubRelease release in pageReleases)
            {
                if (release.Draft)
                {
                    continue;
                }

                if (!includePrereleases && release.Prerelease)
                {
                    continue;
                }

                GXGitHubAsset? asset = SelectAsset(release.Assets, target.AssetPattern);
                releases.Add(new GXUpdateRelease
                {
                    Version = NormalizeListedVersion(release.TagName),
                    TagName = release.TagName,
                    IsPrerelease = release.Prerelease,
                    PublishedAt = release.PublishedAt,
                    ReleaseNotes = release.Body,
                    ReleaseUrl = release.HtmlUrl,
                    Asset = CreateUpdateAsset(asset)
                });
            }

            ++page;
        }

        return releases
            .OrderByDescending(x => x.PublishedAt ?? DateTimeOffset.MinValue)
            .Take(count)
            .ToArray();
    }

    /// <summary>
    /// Checks targets with bounded concurrency, preserving input order and capturing individual failures.
    /// </summary>
    public async Task<IReadOnlyList<GXUpdateCheckResult>> CheckAsync(
        IEnumerable<GXUpdateTarget> targets,
        int maxConcurrency,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (maxConcurrency < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
        }

        GXUpdateTarget[] items = targets.ToArray();
        GXUpdateCheckResult[] results = new GXUpdateCheckResult[items.Length];
        using SemaphoreSlim semaphore = new(maxConcurrency);
        IEnumerable<Task> tasks = items.Select(async (target, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                ValidateTarget(target);
                GXUpdateInfo update = await CheckAsync(target, cancellationToken);
                results[index] = new GXUpdateCheckResult
                {
                    Name = target.Name!,
                    Type = target.Type,
                    Update = update
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results[index] = new GXUpdateCheckResult
                {
                    Name = target.Name!,
                    Type = target.Type,
                    Error = ex.Message
                };
            }
            finally
            {
                semaphore.Release();
            }
        });
        await Task.WhenAll(tasks);
        return results;
    }

    /// <summary>
    /// Validates the target name, version source, and repository for a batch check.
    /// </summary>
    private static void ValidateTarget(GXUpdateTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.Name))
        {
            throw new ArgumentException("Update target name is required.");
        }

        if (string.IsNullOrWhiteSpace(target.Application) &&
            string.IsNullOrWhiteSpace(target.CurrentVersion))
        {
            throw new ArgumentException($"Application or CurrentVersion is required for '{target.Name}'.");
        }

        if (string.IsNullOrWhiteSpace(target.Repository) ||
            !target.Repository.Contains('/'))
        {
            throw new ArgumentException($"Repository owner/name is required for '{target.Name}'.");
        }
    }

    /// <summary>
    /// Validates the target and count for release discovery.
    /// </summary>
    private static void ValidateReleaseQueryTarget(GXUpdateTarget target, int count)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (!TryGetRepositoryParts(target.Repository, out _, out _))
        {
            throw new ArgumentException(
                string.IsNullOrWhiteSpace(target.Name)
                    ? "Repository owner/name is required."
                    : $"Repository owner/name is required for '{target.Name}'.",
                nameof(target));
        }
    }

    /// <summary>
    /// Downloads a release asset to a file, overwriting any existing destination file.
    /// </summary>
    public async Task DownloadAsync(
        string url,
        string destination,
        IProgress<GXUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _client.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? 0;
        long bytesReceived = 0;
        byte[] buffer = new byte[81920];

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream target = File.Create(destination);

        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            bytesReceived += read;
            progress?.Report(new GXUpdateProgress
            {
                BytesReceived = bytesReceived,
                TotalBytes = totalBytes
            });
        }
    }

    /// <summary>
    /// Downloads a release asset to a file, overwriting any existing destination file.
    /// </summary>
    public Task DownloadAsync(
        string url,
        string destination,
        CancellationToken cancellationToken)
    {
        return DownloadAsync(url, destination, null, cancellationToken);
    }

    /// <summary>
    /// Reads the assembly version, falling back to file version metadata for a non-managed binary.
    /// </summary>
    private static string GetApplicationVersion(string application)
    {
        string path = Path.GetFullPath(application);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Application was not found.", path);
        }
        try
        {
            AssemblyName name = AssemblyName.GetAssemblyName(path);
            return name.Version?.ToString() ?? "0.0.0";
        }
        catch (BadImageFormatException)
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
            return info.ProductVersion?.Split('+')[0] ?? info.FileVersion ?? "0.0.0";
        }
    }

    /// <summary>
    /// Returns the explicit current version or reads it from the target application.
    /// </summary>
    private static string GetCurrentVersion(GXUpdateTarget target)
    {
        if (!string.IsNullOrWhiteSpace(target.CurrentVersion))
        {
            return target.CurrentVersion;
        }

        if (!string.IsNullOrWhiteSpace(target.Application))
        {
            return GetApplicationVersion(target.Application);
        }

        throw new InvalidOperationException(
            "CurrentVersion or Application must be specified.");
    }

    /// <summary>
    /// Selects an asset by name pattern, or prefers an operating system and architecture ZIP match before any ZIP asset.
    /// </summary>
    private static GXGitHubAsset? SelectAsset(IReadOnlyList<GXGitHubAsset> assets, string? pattern)
    {
        if (!string.IsNullOrWhiteSpace(pattern))
        {
            return assets.FirstOrDefault(a => WildcardMatch(a.Name, pattern));
        }
        string os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "osx";
        string arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        string[] names = arch == "x64" ? ["x64", "amd64"] : arch == "arm64" ? ["arm64", "aarch64"] : [arch];
        return assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            && a.Name.Contains(os, StringComparison.OrdinalIgnoreCase)
            && names.Any(n => a.Name.Contains(n, StringComparison.OrdinalIgnoreCase)))
            ?? assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Creates a public update asset from the GitHub asset metadata.
    /// </summary>
    private static GXUpdateAsset? CreateUpdateAsset(GXGitHubAsset? asset)
    {
        return asset is null
            ? null
            : new GXUpdateAsset
            {
                Name = asset.Name,
                DownloadUrl = asset.BrowserDownloadUrl,
                Size = asset.Size
            };
    }

    /// <summary>
    /// Determines whether the asterisk-separated pattern parts occur in order, ignoring case.
    /// </summary>
    private static bool WildcardMatch(string value, string pattern)
    {
        string[] parts = pattern.Split('*');
        int position = 0;
        foreach (string part in parts)
        {
            if (part.Length == 0)
            {
                continue;
            }
            int index = value.IndexOf(part, position, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }
            position = index + part.Length;
        }
        return true;
    }

    /// <summary>
    /// Detects a container using DOTNET_RUNNING_IN_CONTAINER or the /.dockerenv file.
    /// </summary>
    private static bool IsContainer()
    {
        return string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase)
            || File.Exists("/.dockerenv");
    }

    /// <summary>
    /// Compares normalized numeric versions, falling back to a case-insensitive comparison of the original strings.
    /// </summary>
    private static int CompareVersions(string left, string right)
    {
        if (Version.TryParse(NormalizeVersion(left), out Version? a) && Version.TryParse(NormalizeVersion(right), out Version? b))
        {
            return a.CompareTo(b);
        }
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Trims whitespace and leading v characters, then removes the suffix starting at the first hyphen.
    /// </summary>
    private static string NormalizeVersion(string value)
    {
        string version = value.Trim().TrimStart('v', 'V');
        int dash = version.IndexOf('-');
        return dash >= 0 ? version[..dash] : version;
    }

    /// <summary>
    /// Trims whitespace and leading v characters while preserving any prerelease suffix.
    /// </summary>
    private static string NormalizeListedVersion(string value)
    {
        return value.Trim().TrimStart('v', 'V');
    }

    /// <summary>
    /// Sends a GET request and deserializes the JSON response body.
    /// </summary>
    private async Task<T> GetJsonAsync<T>(
        string requestUri,
        string emptyResponseMessage,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _client.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        T? value = await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken);
        return value ?? throw new InvalidDataException(emptyResponseMessage);
    }

    /// <summary>
    /// Builds the GitHub releases request URI for the specified repository page.
    /// </summary>
    private static string BuildReleasesRequestUri(string repository, int page)
    {
        _ = TryGetRepositoryParts(repository, out string owner, out string name);
        return $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}/releases?per_page={ReleasePageSize}&page={page}";
    }

    /// <summary>
    /// Parses an owner/name repository value.
    /// </summary>
    private static bool TryGetRepositoryParts(string? repository, out string owner, out string name)
    {
        owner = string.Empty;
        name = string.Empty;

        if (string.IsNullOrWhiteSpace(repository))
        {
            return false;
        }

        string[] parts = repository.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            string.IsNullOrWhiteSpace(parts[0]) ||
            string.IsNullOrWhiteSpace(parts[1]))
        {
            return false;
        }

        owner = parts[0];
        name = parts[1];
        return true;
    }
}
