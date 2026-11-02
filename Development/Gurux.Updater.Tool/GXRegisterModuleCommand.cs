using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Gurux.Updater.Services;

namespace Gurux.Updater.Tool;

/// <summary>Registers an existing module project in local Modules and Catalog registries.</summary>
public static class GXRegisterModuleCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, NewLine = "\n" };
    /// <summary>Command usage.</summary>
    public const string HelpText = """
        register-module --project <csproj> --modules-root <directory> --catalog-root <directory>
          [--id <Gurux.AMI.Module>] [--name <display-name>] [--description <text>]
          [--repository <owner/repository>] [--category <slug>] [--audience <slug>]
          [--maintenance-status <active|minimal|seeking-maintainer|unsupported>]
          [--icon <project-relative-image-path>]
          [--dry-run] [--json]
        Reads evaluated MSBuild properties without building or restoring the project.
        Requires net10.0 and AmiModuleCompatibilityMetadata=true. Existing registry files
        and release targets are required. No commits, remote changes or releases are made.
        --dry-run previews both resulting registries without writing files.
        """;

    /// <summary>Validates, previews or applies registration in both local repositories.</summary>
    public static async Task RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken = default)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            await output.WriteLineAsync(HelpText);
            return;
        }
        string[] allowed = ["--project", "--modules-root", "--catalog-root", "--id", "--name", "--description",
            "--repository", "--category", "--audience", "--maintenance-status", "--icon", "--dry-run", "--json"];
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int pos = 0; pos < args.Length; ++pos)
        {
            string key = args[pos];
            if (!allowed.Contains(key) || options.ContainsKey(key))
                throw new ArgumentException($"Unknown or duplicate option '{key}'.");
            string value = "true";
            if (key is not ("--dry-run" or "--json"))
            {
                if (++pos >= args.Length || args[pos].StartsWith("--", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(args[pos]))
                    throw new ArgumentException($"Missing value for {key}.");
                value = args[pos];
            }
            options.Add(key, value);
        }
        string Required(string key) => options.GetValueOrDefault(key) ?? throw new ArgumentException($"{key} is required.");
        foreach (string field in new[] { "category", "audience", "maintenanceStatus" })
        {
            string key = field == "maintenanceStatus" ? "--maintenance-status" : "--" + field;
            try { GXCatalogMetadata.ValidateClassification(options.GetValueOrDefault(key), field); }
            catch (InvalidDataException ex) { throw new ArgumentException(ex.Message, ex); }
        }
        if (options.TryGetValue("--id", out var suppliedId)) ValidateId(suppliedId);
        if (options.TryGetValue("--repository", out var suppliedRepository)) ValidateRepository(suppliedRepository);

        string modulesRoot = Path.GetFullPath(Required("--modules-root"));
        string catalogRoot = Path.GetFullPath(Required("--catalog-root"));
        string project = Path.GetFullPath(Required("--project"));
        string modulesPath = Path.Combine(modulesRoot, "modules.json");
        string catalogPath = Path.Combine(catalogRoot, "catalog-sources.json");
        string relativeProject = Path.GetRelativePath(modulesRoot, project).Replace('\\', '/');
        if (relativeProject.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relativeProject) ||
            !relativeProject.Contains('/') || !project.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Project must be a .csproj in a subdirectory of --modules-root.");
        foreach (string path in new[] { project, modulesPath, catalogPath }) CheckRegularPath(path);

        byte[] originalModules = await File.ReadAllBytesAsync(modulesPath, cancellationToken);
        byte[] originalCatalog = await File.ReadAllBytesAsync(catalogPath, cancellationToken);
        var modules = ReadRegistry(originalModules);
        var catalog = ReadRegistry(originalCatalog);
        if (modules["modules"] is not JsonObject entries || catalog["items"] is not JsonArray items)
            throw new InvalidDataException("Expected modules.json modules object and catalog-sources.json items array.");
        var moduleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
            if (!moduleIds.Add(entry.Key) || entry.Value is not JsonObject)
                throw new InvalidDataException("Duplicate module ID or invalid module entry.");
        var catalogIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
            if (item is not JsonObject obj || obj["id"] is not JsonValue idValue ||
                !idValue.TryGetValue<string>(out string? itemId) || string.IsNullOrWhiteSpace(itemId) || !catalogIds.Add(itemId))
                throw new InvalidDataException("Duplicate catalog ID or invalid catalog entry.");

        var properties = await EvaluateAsync(project, cancellationToken);
        string Get(string key) => properties.GetProperty(key).GetString() ?? string.Empty;
        if (!Get("AmiModuleCompatibilityMetadata").Equals("true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Set <AmiModuleCompatibilityMetadata>true</AmiModuleCompatibilityMetadata> in the project or its imported properties before registration.");
        if (Get("TargetFramework") != "net10.0")
            throw new InvalidDataException("Module releases require TargetFramework net10.0.");
        string packageId = string.IsNullOrWhiteSpace(Get("PackageId")) ? Get("AssemblyName") : Get("PackageId");
        string id = options.GetValueOrDefault("--id") ?? packageId;
        ValidateId(id);
        if (id != packageId)
            throw new InvalidDataException("--id must match the evaluated PackageId: release ZIP and compatibility filenames use PackageId.");
        await ValidateTargetsAsync(project, cancellationToken);
        string repository = options.GetValueOrDefault("--repository") ?? RepositoryFromUrl(Get("RepositoryUrl"));
        ValidateRepository(repository);
        string projectDirectory = relativeProject[..relativeProject.LastIndexOf('/')];
        string? matchingModule = entries.Select(e => e.Key).FirstOrDefault(key => key.Equals(id, StringComparison.OrdinalIgnoreCase));
        JsonObject? module = matchingModule is null ? null : (JsonObject)entries[matchingModule]!;
        if (matchingModule is not null && matchingModule != id)
            throw new InvalidDataException("A module ID differing only in case already exists.");
        foreach (var entry in entries)
        {
            string? existingPath = Text(entry.Value!["project"]);
            if (entry.Key != id && existingPath is not null &&
                Path.GetFullPath(Path.Combine(modulesRoot, existingPath)).Equals(project, PathComparison))
                throw new InvalidDataException($"Project is already registered as '{entry.Key}'.");
        }
        if (module is not null && Text(module["project"]) != relativeProject)
            throw new InvalidDataException($"Module '{id}' is already registered to a different project.");
        var existingItem = items.OfType<JsonObject>().SingleOrDefault(i => Text(i["id"])!.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existingItem is not null)
        {
            foreach (var pair in new Dictionary<string, string> { ["id"] = id, ["type"] = "module", ["repository"] = repository,
                ["tagPrefix"] = id + "/v", ["assetPattern"] = id + "-*.zip" })
                if (existingItem[pair.Key] is not null && Text(existingItem[pair.Key]) != pair.Value)
                    throw new InvalidDataException($"Existing catalog entry '{id}' conflicts on '{pair.Key}'.");
            if (existingItem["compatibilityMetadata"] is not null && existingItem["compatibilityMetadata"]!.GetValue<bool>() != true)
                throw new InvalidDataException("Existing catalog entry has compatibilityMetadata disabled.");
        }
        if (module is null)
        {
            module = new JsonObject { ["project"] = relativeProject };
            entries.Add(id, module);
        }
        if (module["changelogPaths"] is null) module["changelogPaths"] = new JsonArray();
        if (module["changelogPaths"] is not JsonArray changelog)
            throw new InvalidDataException("Module changelogPaths must be an array of repository-relative paths.");
        var paths = changelog.Select(n => Text(n) ?? throw new InvalidDataException("Invalid changelog path.")).ToHashSet(StringComparer.Ordinal);
        foreach (string path in paths)
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Replace('\\', '/').Split('/').Any(p => p is "." or ".."))
                throw new InvalidDataException("Module changelogPaths must be repository-relative paths.");
        foreach (string path in new[] { projectDirectory, "Directory.Build.props", "Directory.Build.targets", "NuGet.Config", "scripts/Gurux.AMI.Module.Metadata" })
            if (paths.Add(path)) changelog.Add(path);
        if (existingItem is null)
        {
            existingItem = new JsonObject { ["id"] = id, ["type"] = "module", ["repository"] = repository,
                ["tagPrefix"] = id + "/v", ["assetPattern"] = id + "-*.zip" };
            items.Add(existingItem);
        }
        existingItem["type"] = "module";
        existingItem["repository"] = repository;
        existingItem["tagPrefix"] = id + "/v";
        existingItem["assetPattern"] = id + "-*.zip";
        existingItem["compatibilityMetadata"] = true;
        string inferredName = string.IsNullOrWhiteSpace(Get("Title")) ? id["Gurux.AMI.".Length..] : Get("Title");
        string inferredDescription = string.IsNullOrWhiteSpace(Get("Description")) || Get("Description") == "Package Description"
            ? inferredName + " module for Gurux DLMS AMI." : Get("Description");
        foreach (var pair in new Dictionary<string, string> { ["name"] = inferredName, ["description"] = inferredDescription,
            ["maintenanceStatus"] = "active", ["category"] = string.Empty, ["audience"] = string.Empty })
        {
            string option = pair.Key == "maintenanceStatus" ? "--maintenance-status" : "--" + pair.Key;
            if (options.TryGetValue(option, out string? value)) existingItem[pair.Key] = value;
            else if (existingItem[pair.Key] is null && pair.Value.Length != 0) existingItem[pair.Key] = pair.Value;
        }
        ConfigureIcon(options, existingItem, project, modulesRoot, id);
        var files = new[] { new RegistryFile(modulesPath, originalModules, modules), new RegistryFile(catalogPath, originalCatalog, catalog) };
        bool dryRun = options.ContainsKey("--dry-run");
        if (!dryRun) await WriteRegistriesAsync(files, cancellationToken);
        if (options.ContainsKey("--json"))
            await output.WriteLineAsync(JsonSerializer.Serialize(new { id, dryRun, files = files.Select(f => new { path = f.Path, changed = f.Changed, content = f.Content }) }, JsonOptions));
        else
        {
            await output.WriteLineAsync($"{(dryRun ? "Registration preview" : "Registered")}: {id}");
            foreach (var file in files)
            {
                await output.WriteLineAsync($"{(file.Changed ? dryRun ? "Would update" : "Updated" : "Unchanged")}: {file.Path}");
                if (dryRun && file.Changed) await output.WriteLineAsync(file.Content.ToJsonString(JsonOptions));
            }
        }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static void ConfigureIcon(Dictionary<string, string> options, JsonObject item, string project, string modulesRoot, string id)
    {
        string directory = Path.GetDirectoryName(project)!;
        string[] extensions = [".svg", ".png", ".jpg", ".jpeg", ".webp", ".gif", ".ico"];
        string? icon = options.GetValueOrDefault("--icon");
        if (icon is null)
        {
            if (item["iconPath"] is not null || item["iconUrl"] is not null) return;
            string assets = Path.Combine(directory, "assets");
            if (!Directory.Exists(assets)) return;
            var candidates = Directory.GetFiles(assets).Where(p => extensions.Contains(Path.GetExtension(p).ToLowerInvariant())).ToArray();
            if (candidates.Length == 0) return;
            if (candidates.Length > 1) throw new InvalidDataException("Multiple module images found; use --icon <project-relative-path> to select the catalog icon.");
            icon = Path.GetRelativePath(directory, candidates[0]);
        }
        if (Path.IsPathRooted(icon) || icon.Replace('\\', '/').Split('/').Any(p => p is ".." or "."))
            throw new InvalidDataException("--icon must be a relative path inside the module project directory.");
        string full = Path.GetFullPath(Path.Combine(directory, icon));
        if (!extensions.Contains(Path.GetExtension(full).ToLowerInvariant()))
            throw new InvalidDataException("Unsupported module icon extension.");
        CheckRegularPath(full);
        item["iconPath"] = Path.GetRelativePath(modulesRoot, full).Replace('\\', '/');
        item["iconUrl"] = "https://gurux.github.io/Gurux.AMI.Catalog/assets/" + Uri.EscapeDataString(id) + "/" + Uri.EscapeDataString(Path.GetFileName(full));
    }
    private static string? Text(JsonNode? node) => node is null ? null : node is JsonValue value && value.TryGetValue<string>(out var text)
        ? text : throw new InvalidDataException("Expected a string in registry metadata.");
    private static void ValidateId(string id)
    {
        if (id.Length > 150 || !Regex.IsMatch(id, @"^Gurux\.AMI\.[A-Za-z0-9]+(?:[_.-][A-Za-z0-9]+)*$"))
            throw new ArgumentException("Module ID must be a Gurux.AMI.* identifier.");
    }
    private static void ValidateRepository(string repository)
    {
        if (!Regex.IsMatch(repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$") || repository.Split('/').Any(p => p is "." or ".."))
            throw new ArgumentException("--repository must be owner/repository.");
    }
    private static string RepositoryFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "Gurux/Gurux.AMI.Modules";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
            throw new InvalidDataException("RepositoryUrl must be an HTTPS GitHub repository URL, or provide --repository owner/repository.");
        string path = uri.AbsolutePath.Trim('/');
        return path.EndsWith(".git", StringComparison.Ordinal) ? path[..^4] : path;
    }
    private static void CheckRegularPath(string path)
    {
        if (!File.Exists(path)) throw new InvalidDataException($"Required file does not exist: {path}");
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Symlinks and reparse points are not supported: {current}");
    }
    private static JsonObject ReadRegistry(byte[] bytes)
    {
        try
        {
            string text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
            using var document = JsonDocument.Parse(text);
            CheckDuplicates(document.RootElement);
            return JsonNode.Parse(text) as JsonObject ?? throw new InvalidDataException("Registry root must be an object.");
        }
        catch (JsonException ex) { throw new InvalidDataException("Invalid registry JSON.", ex); }
    }
    private static void CheckDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException($"Duplicate JSON property '{property.Name}'.");
                CheckDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CheckDuplicates(item);
    }
    private static async Task<JsonElement> EvaluateAsync(string project, CancellationToken cancellationToken)
    {
        string text = await RunMsBuildAsync(project,
            "-getProperty:PackageId,AssemblyName,Title,Description,RepositoryUrl,AmiModuleCompatibilityMetadata,TargetFramework", cancellationToken);
        try { using var json = JsonDocument.Parse(text); return json.RootElement.GetProperty("Properties").Clone(); }
        catch (JsonException ex) { throw new InvalidDataException("MSBuild did not return valid project properties.", ex); }
    }
    private static async Task ValidateTargetsAsync(string project, CancellationToken cancellationToken)
    {
        string text = await RunMsBuildAsync(project, "-preprocess", cancellationToken);
        var document = XDocument.Parse(text);
        var targets = document.Descendants().Where(e => e.Name.LocalName == "Target")
            .Select(e => (string?)e.Attribute("Name")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string target in new[] { "ZipPublish", "AmiWriteCompatibilityMetadata" })
            if (!targets.Contains(target))
                throw new InvalidDataException($"Module project must define or import the '{target}' release target before registration.");
    }
    private static async Task<string> RunMsBuildAsync(string project, string query, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(project) };
        foreach (string arg in new[] { "msbuild", project, "-nologo", "-verbosity:quiet", "-p:Configuration=Release", query }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start dotnet msbuild.");
        using var registration = cancellationToken.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string text = await stdout, error = await stderr;
        if (process.ExitCode != 0) throw new InvalidDataException($"MSBuild project evaluation failed: {error}{text}");
        return text;
    }
    private sealed record RegistryFile(string Path, byte[] Original, JsonObject Content)
    {
        public bool Changed => !JsonNode.DeepEquals(ReadRegistry(Original), Content);
    }
    private static async Task WriteRegistriesAsync(RegistryFile[] files, CancellationToken cancellationToken)
    {
        var staged = new List<(RegistryFile File, string Temporary)>();
        int committed = 0;
        try
        {
            foreach (var file in files.Where(f => f.Changed))
            {
                string temporary = file.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                staged.Add((file, temporary));
                await File.WriteAllTextAsync(temporary, file.Content.ToJsonString(JsonOptions) + "\n", new UTF8Encoding(false), cancellationToken);
            }
            foreach (var file in files)
                if (!(await File.ReadAllBytesAsync(file.Path, cancellationToken)).AsSpan().SequenceEqual(file.Original))
                    throw new IOException($"Registry changed during registration: {file.Path}. Retry the command.");
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var item in staged)
            {
                File.Move(item.Temporary, item.File.Path, overwrite: true);
                ++committed;
            }
        }
        catch
        {
            foreach (var item in staged.Take(committed)) File.WriteAllBytes(item.File.Path, item.File.Original);
            throw;
        }
        finally
        {
            foreach (var item in staged)
                try { File.Delete(item.Temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
