using RaceReplay.Bundle;
using RaceReplay.Bundle.Catalog;
using RaceReplay.Bundle.Manifest;
using RaceReplay.Domain;

namespace RaceReplay.Web.Loading;

/// <summary>
/// Loads replays served with the site: demo races, and real races in hosted mode (docs/05 §9.1, ADR-0008). The id is
/// the catalogue entry's id, e.g. <c>demo-1</c>.
/// </summary>
/// <remarks>
/// The only replay source until local packages arrive (RR-053), when the shared <c>IReplaySource</c> interface is
/// extracted (ADR-0009).
/// </remarks>
public sealed class HttpReplaySource(HttpClient http, SiteCatalog catalog)
{
    /// <summary>Largest bundle file the app downloads, matching the local package cap (docs/05 §9.2).</summary>
    public const long MaxFileBytes = 64 * 1024 * 1024;

    // Share of the progress bar per step: the catalogue and manifest are small; the channel files are the bulk.
    private const double ManifestDone = 0.05;
    private const double ChannelsDone = 0.95;

    /// <summary>Downloads and reads a replay, reporting progress from 0 to 1.</summary>
    /// <exception cref="ReplayLoadException">The replay isn't in the catalogue, couldn't be fetched, or isn't valid.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<ReplaySession> LoadAsync(string id, IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(progress);
        progress.Report(0);

        BundleCatalogDto sessions;
        try
        {
            sessions = await catalog.GetAsync(cancellationToken);
        }
        catch (ReplayLoadException e)
        {
            throw BundleDownload.Failed(e.Failure, $"The catalogue couldn't be loaded. {e.Message}", id, e);
        }

        CatalogSessionDto entry = sessions.Sessions.FirstOrDefault(s => s.Id == id)
            ?? throw BundleDownload.Failed(ReplayLoadFailure.NotFound, $"'{id}' isn't in the catalogue.", id);
        string folder = SiteCatalog.BundlesFolder + entry.Path;

        byte[] manifestJson = await BundleDownload.GetAsync(
            http, folder + BundleFiles.Manifest, id, MaxFileBytes, expectedBytes: null, onRead: null, cancellationToken);
        BundleManifestDto manifest = Read(id, () => ReplayBundleReader.ReadManifest(manifestJson));
        progress.Report(ManifestDone);

        long expected = manifest.Channels.Positions.Bytes;
        if (expected is <= 0 or > MaxFileBytes)
        {
            throw BundleDownload.Failed(ReplayLoadFailure.Corrupt, $"The manifest gives {BundleFiles.Positions} an invalid size ({expected}).", id);
        }

        byte[] positions = await BundleDownload.GetAsync(
            http,
            folder + BundleFiles.Positions,
            id,
            maxBytes: expected,
            expectedBytes: expected,
            read => progress.Report(ManifestDone + ((ChannelsDone - ManifestDone) * read / expected)),
            cancellationToken);

        ReplaySession replay = Read(id, () => ReplayBundleReader.Read(manifestJson, positions));
        progress.Report(1);
        return replay;
    }

    private static T Read<T>(string id, Func<T> read)
    {
        try
        {
            return read();
        }
        catch (BundleFormatException e)
        {
            throw BundleDownload.Failed(e.IsNewerVersion ? ReplayLoadFailure.NewerVersion : ReplayLoadFailure.Corrupt, e.Message, id, e);
        }
    }
}