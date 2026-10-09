namespace Gurux.Updater.Services;

/// <summary>Reads local catalog files or HTTPS resources and resolves their relative references.</summary>
public sealed class GXCatalogSourceReader
{
    private readonly HttpClient client;
    private readonly bool allowLoopbackHttp;
    private readonly bool allowHttp;

    /// <summary>Creates a reader. Loopback HTTP can be enabled for existing development catalogs.</summary>
    public GXCatalogSourceReader(HttpClient client, bool allowLoopbackHttp = false, bool allowHttp = false)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.allowLoopbackHttp = allowLoopbackHttp;
        this.allowHttp = allowHttp;
    }

    /// <summary>Returns a validated remote address, or null for a local path including Windows drive paths.</summary>
    public Uri? GetRemoteUri(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (Path.IsPathRooted(source) || source.Length >= 2 && char.IsLetter(source[0]) && source[1] == ':' || source.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return null;
        }
        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri))
        {
            if (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && (allowHttp || allowLoopbackHttp && uri.IsLoopback))
            {
                return uri;
            }
            throw new ArgumentException($"Source '{source}' must be a local path or an HTTPS address.", nameof(source));
        }
        return null;
    }

    /// <summary>Resolves a package reference relative to the catalog file or URL, preserving website subdirectories.</summary>
    public string ResolveReference(string catalogSource, string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        Uri? catalogUri = GetRemoteUri(catalogSource);
        Uri? referenceUri = GetRemoteUri(reference);
        if (referenceUri != null)
        {
            return referenceUri.AbsoluteUri;
        }
        if (catalogUri != null)
        {
            if (Path.IsPathRooted(reference) && !reference.StartsWith('/') || reference.Contains('\\') || reference.Length >= 2 && reference[1] == ':')
            {
                throw new InvalidDataException("An HTTPS catalog cannot reference a local filesystem path.");
            }
            Uri resolved = new(catalogUri, reference);
            _ = GetRemoteUri(resolved.AbsoluteUri);
            return resolved.AbsoluteUri;
        }
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(catalogSource))!, reference.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>Copies a local or HTTPS source to a stream, reporting the existing updater progress type.</summary>
    public async Task CopyToAsync(string source, Stream destination, IProgress<GXUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Uri? uri = GetRemoteUri(source);
        try
        {
            if (uri == null)
            {
                await using FileStream input = new(Path.GetFullPath(source), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
                await CopyAsync(input, destination, input.Length, progress, cancellationToken);
                return;
            }
            using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Reading '{source}' failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);
            }
            // Validate the final URI as well when an injected client follows redirects.
            if (response.RequestMessage?.RequestUri is Uri finalUri)
            {
                _ = GetRemoteUri(finalUri.AbsoluteUri);
            }
            await using Stream inputStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await CopyAsync(inputStream, destination, response.Content.Headers.ContentLength ?? 0, progress, cancellationToken);
        }
        catch (IOException ex)
        {
            throw new IOException($"Could not read '{source}': {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException($"Could not read '{source}': access denied.", ex);
        }
    }

    /// <summary>Reads a source into memory for JSON validation.</summary>
    public async Task<byte[]> ReadAsync(string source, CancellationToken cancellationToken = default)
    {
        using MemoryStream stream = new();
        await CopyToAsync(source, stream, cancellationToken: cancellationToken);
        return stream.ToArray();
    }

    private static async Task CopyAsync(Stream input, Stream output, long total, IProgress<GXUpdateProgress>? progress, CancellationToken token)
    {
        byte[] buffer = new byte[81920];
        long received = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, count), token);
            received += count;
            progress?.Report(new GXUpdateProgress { BytesReceived = received, TotalBytes = total });
            token.ThrowIfCancellationRequested();
        }
    }
}
