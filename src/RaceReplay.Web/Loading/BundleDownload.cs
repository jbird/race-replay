namespace RaceReplay.Web.Loading;

/// <summary>Downloads one file of the site's bundles folder, with a size cap and progress, mapping failures to <see cref="ReplayLoadException"/>.</summary>
internal static class BundleDownload
{
    private const int ChunkBytes = 64 * 1024;

    /// <param name="http">The client whose base address is the site root.</param>
    /// <param name="path">The file, relative to the site root.</param>
    /// <param name="replayId">The replay being loaded, for error reports; <see langword="null"/> for the catalogue.</param>
    /// <param name="maxBytes">Largest size accepted, so a bad manifest or server can't make the app allocate without limit.</param>
    /// <param name="expectedBytes">The size when the caller knows it, so the buffer is allocated once; otherwise <c>Content-Length</c> is the hint.</param>
    /// <param name="onRead">Called with the bytes read so far after each chunk.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <exception cref="ReplayLoadException">The request failed, or the file is larger than <paramref name="maxBytes"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static async Task<byte[]> GetAsync(
        HttpClient http,
        string path,
        string? replayId,
        long maxBytes,
        long? expectedBytes,
        Action<long>? onRead,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw Failed(ReplayLoadFailure.Unavailable, $"{path}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}", replayId);
            }

            // WebAssembly memory never shrinks, so avoid MemoryStream's doubling and a final copy when the size is known.
            long capacity = Math.Clamp(expectedBytes ?? response.Content.Headers.ContentLength ?? 0, 0, maxBytes);
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using MemoryStream buffer = new((int)capacity);
            byte[] chunk = new byte[ChunkBytes];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    throw Failed(ReplayLoadFailure.Corrupt, $"{path} is larger than {maxBytes:N0} bytes.", replayId);
                }

                buffer.Write(chunk, 0, read);
                onRead?.Invoke(buffer.Length);
            }

            return buffer.Length == buffer.Capacity ? buffer.GetBuffer() : buffer.ToArray();
        }
        catch (Exception e) when (cancellationToken.IsCancellationRequested && e is HttpRequestException or IOException)
        {
            // A cancelled browser fetch can surface as a network error; report it as the cancellation it is.
            throw new OperationCanceledException($"{path}: the download was cancelled.", e, cancellationToken);
        }
        catch (HttpRequestException e)
        {
            throw Failed(ReplayLoadFailure.Unavailable, $"{path}: {e.Message}", replayId, e);
        }
        catch (IOException e)
        {
            throw Failed(ReplayLoadFailure.Unavailable, $"{path}: {e.Message}", replayId, e);
        }
        catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failed(ReplayLoadFailure.Unavailable, $"{path}: the request timed out.", replayId, e);
        }
    }

    public static ReplayLoadException Failed(ReplayLoadFailure failure, string message, string? replayId, Exception? inner = null) =>
        inner is null
            ? new ReplayLoadException(message) { Failure = failure, ReplayId = replayId }
            : new ReplayLoadException(message, inner) { Failure = failure, ReplayId = replayId };
}