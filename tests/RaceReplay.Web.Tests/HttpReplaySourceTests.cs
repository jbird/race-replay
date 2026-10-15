using System.Net;
using System.Text;

using RaceReplay.Bundle;
using RaceReplay.Bundle.Catalog;
using RaceReplay.Domain;
using RaceReplay.Web.Loading;

namespace RaceReplay.Web.Tests;

// These clauses become ReplaySourceContract when LocalPackageReplaySource arrives (RR-053, 06 §6).
public sealed class HttpReplaySourceTests : IDisposable
{
    private readonly FakeBundleSite _site = new();

    public void Dispose() => _site.Dispose();

    [Fact]
    public async Task Load_DemoInCatalogue_ReturnsTheReplayTheBundleBytesHold()
    {
        ReplaySession expected = ReplayBundleReader.Read(_site.Bundle.ManifestJson.Span, _site.Bundle.Positions.Span);

        ReplaySession loaded = await Load(FakeBundleSite.DemoId);

        Assert.Equal(expected.Session, loaded.Session);
        Assert.Equal(expected.Drivers, loaded.Drivers);
        Assert.Equal(expected.Provenance, loaded.Provenance);
        Assert.Equal(expected.Timeline.SampleCount, loaded.Timeline.SampleCount);
        Assert.Equal(expected.Timeline.Duration, loaded.Timeline.Duration);
        Assert.Equal(expected.Track.Centreline.ToArray(), loaded.Track.Centreline.ToArray());
        Assert.All(expected.Cars.Zip(loaded.Cars), pair =>
        {
            Assert.Equal(pair.First.X.ToArray(), pair.Second.X.ToArray());
            Assert.Equal(pair.First.Y.ToArray(), pair.Second.Y.ToArray());
            Assert.Equal(pair.First.Valid.Bits.ToArray(), pair.Second.Valid.Bits.ToArray());
        });
    }

    [Fact]
    public async Task Load_DemoInCatalogue_ReportsRisingProgressFromZeroToOne()
    {
        RecordingProgress progress = new();

        await Load(FakeBundleSite.DemoId, progress);

        Assert.Equal(0, progress.Values[0]);
        Assert.Equal(1, progress.Values[^1]);
        Assert.All(progress.Values.Zip(progress.Values.Skip(1)), p => Assert.True(p.Second >= p.First, $"{p.Second} after {p.First}"));
    }

    [Fact]
    public async Task Load_IdNotInCatalogue_ThrowsNotFound()
    {
        ReplayLoadException e = await Assert.ThrowsAsync<ReplayLoadException>(() => Load("demo-999"));

        Assert.Equal(ReplayLoadFailure.NotFound, e.Failure);
        Assert.Equal("demo-999", e.ReplayId);
    }

    [Theory]
    [InlineData(BundleFiles.Manifest, HttpStatusCode.NotFound)]
    [InlineData(BundleFiles.Positions, HttpStatusCode.NotFound)]
    [InlineData(BundleFiles.Positions, HttpStatusCode.InternalServerError)]
    public async Task Load_BundleFileFails_ThrowsUnavailableNamingTheStatus(string file, HttpStatusCode status)
    {
        _site.ServeStatus(FakeBundleSite.DemoFolder + file, status);

        ReplayLoadException e = await Assert.ThrowsAsync<ReplayLoadException>(() => Load(FakeBundleSite.DemoId));

        Assert.Equal(ReplayLoadFailure.Unavailable, e.Failure);
        Assert.Equal(FakeBundleSite.DemoId, e.ReplayId);
        Assert.Contains(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_NetworkFails_ThrowsUnavailable()
    {
        _site.ServeFailure(FakeBundleSite.DemoFolder + BundleFiles.Positions, new HttpRequestException("connection reset"));

        ReplayLoadException e = await Assert.ThrowsAsync<ReplayLoadException>(() => Load(FakeBundleSite.DemoId));

        Assert.Equal(ReplayLoadFailure.Unavailable, e.Failure);
    }

    [Fact]
    public async Task Load_NewerSchemaMajor_ThrowsNewerVersion()
    {
        string json = Encoding.UTF8.GetString(_site.Bundle.ManifestJson.Span);
        string newer = json.Replace($"\"schemaVersion\":\"{_site.Bundle.Manifest.SchemaVersion}\"", "\"schemaVersion\":\"99.0\"", StringComparison.Ordinal);
        Assert.NotEqual(json, newer);
        _site.ServeBytes(FakeBundleSite.DemoFolder + BundleFiles.Manifest, Encoding.UTF8.GetBytes(newer));

        ReplayLoadException e = await Assert.ThrowsAsync<ReplayLoadException>(() => Load(FakeBundleSite.DemoId));

        Assert.Equal(ReplayLoadFailure.NewerVersion, e.Failure);
    }

    [Fact]
    public async Task Load_TruncatedPositions_ThrowsCorrupt()
    {
        _site.ServeBytes(FakeBundleSite.DemoFolder + BundleFiles.Positions, _site.Bundle.Positions[..^10].ToArray());

        ReplayLoadException e = await Assert.ThrowsAsync<ReplayLoadException>(() => Load(FakeBundleSite.DemoId));

        Assert.Equal(ReplayLoadFailure.Corrupt, e.Failure);
        Assert.Equal(FakeBundleSite.DemoId, e.ReplayId);
    }

    [Fact]
    public async Task Load_PositionsLargerThanManifestSays_ThrowsCorrupt()
    {
        _site.ServeBytes(FakeBundleSite.DemoFolder + BundleFiles.Positions, [.. _site.Bundle.Positions.Span, 0, 0, 0]);

        ReplayLoadException e = await Assert.ThrowsAsync<ReplayLoadException>(() => Load(FakeBundleSite.DemoId));

        Assert.Equal(ReplayLoadFailure.Corrupt, e.Failure);
    }

    [Fact]
    public async Task Load_MalformedCatalogue_ThrowsCorrupt()
    {
        _site.ServeBytes("bundles/catalog.json", "{ not json"u8.ToArray());

        ReplayLoadException e = await Assert.ThrowsAsync<ReplayLoadException>(() => Load(FakeBundleSite.DemoId));

        Assert.Equal(ReplayLoadFailure.Corrupt, e.Failure);
    }

    [Fact]
    public async Task Load_Cancelled_ThrowsOperationCanceled()
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        HttpReplaySource source = new(_site.CreateClient(), new SiteCatalog(_site.CreateClient()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.LoadAsync(FakeBundleSite.DemoId, new RecordingProgress(), cancelled.Token));
    }

    [Fact]
    public async Task Load_BodyReadFailsAfterCancellation_ThrowsOperationCanceledNotUnavailable()
    {
        using CancellationTokenSource cancel = new();
        _site.ServeStream(FakeBundleSite.DemoFolder + BundleFiles.Positions, () => new FailingStream(() =>
        {
            cancel.Cancel(); // the page navigated away; the browser aborts the fetch
            throw new IOException("The operation was aborted.");
        }));
        HttpClient http = _site.CreateClient();
        HttpReplaySource source = new(http, new SiteCatalog(http));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.LoadAsync(FakeBundleSite.DemoId, new RecordingProgress(), cancel.Token));
    }

    [Fact]
    public async Task Load_Twice_FetchesTheCatalogueOnce()
    {
        HttpClient http = _site.CreateClient();
        HttpReplaySource source = new(http, new SiteCatalog(http));

        await source.LoadAsync(FakeBundleSite.DemoId, new RecordingProgress(), TestContext.Current.CancellationToken);
        await source.LoadAsync(FakeBundleSite.DemoId, new RecordingProgress(), TestContext.Current.CancellationToken);

        Assert.Single(_site.Requests, r => r == "bundles/catalog.json");
    }

    [Fact]
    public async Task Catalogue_AfterAFailedFetch_FetchesAgain()
    {
        HttpClient http = _site.CreateClient();
        SiteCatalog catalog = new(http);
        _site.ServeStatus("bundles/catalog.json", HttpStatusCode.ServiceUnavailable);
        await Assert.ThrowsAsync<ReplayLoadException>(() => catalog.GetAsync(TestContext.Current.CancellationToken));
        _site.ServeBytes("bundles/catalog.json", _site.CatalogueJson);

        BundleCatalogDto sessions = await catalog.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(FakeBundleSite.DemoId, Assert.Single(sessions.Sessions).Id);
    }


    /// <summary>A response body whose first read runs <c>onRead</c>, which throws.</summary>
    private sealed class FailingStream(Action onRead) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            onRead();
            return 0;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            onRead();
            return ValueTask.FromResult(0);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private Task<ReplaySession> Load(string id, IProgress<double>? progress = null)
    {
        HttpClient http = _site.CreateClient();
        return new HttpReplaySource(http, new SiteCatalog(http))
            .LoadAsync(id, progress ?? new RecordingProgress(), TestContext.Current.CancellationToken);
    }
}