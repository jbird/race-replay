using RaceReplay.Bundle;
using RaceReplay.Bundle.Catalog;

namespace RaceReplay.Web.Loading;

/// <summary>
/// The site's <c>bundles/catalog.json</c> (docs/04 §8), fetched on first use and then reused for the rest of the app
/// session. A failed fetch isn't cached, so retrying fetches again.
/// </summary>
public sealed class SiteCatalog(HttpClient http)
{
    /// <summary>The bundles folder, relative to the app's base address.</summary>
    public const string BundlesFolder = "bundles/";

    private const long MaxBytes = 4 * 1024 * 1024;

    private BundleCatalogDto? _catalog;

    /// <exception cref="ReplayLoadException">The catalogue couldn't be fetched or isn't valid.</exception>
    public async Task<BundleCatalogDto> GetAsync(CancellationToken cancellationToken)
    {
        if (_catalog is null)
        {
            byte[] json = await BundleDownload.GetAsync(
                http, BundlesFolder + ReplayCatalog.FileName, replayId: null, MaxBytes, expectedBytes: null, onRead: null, cancellationToken);
            try
            {
                _catalog = ReplayCatalog.Deserialize(json);
            }
            catch (BundleFormatException e)
            {
                throw BundleDownload.Failed(ReplayLoadFailure.Corrupt, e.Message, replayId: null, e);
            }
        }

        return _catalog;
    }
}