using System.Net;

using RaceReplay.Bundle;
using RaceReplay.Bundle.Catalog;
using RaceReplay.Domain;
using RaceReplay.TestKit;

namespace RaceReplay.Web.Tests;

/// <summary>An in-memory site serving <c>bundles/</c> through a fake handler; no network (06 §4).</summary>
internal sealed class FakeBundleSite : HttpMessageHandler
{
    public const string DemoId = "demo-7";
    public const string DemoFolder = "bundles/demo/7/";

    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.Ordinal);
    private readonly List<string> _requests = [];

    public FakeBundleSite()
    {
        Replay = TestReplays.Small(cars: 4);
        Bundle = ReplayBundleWriter.Write(Replay);
        CatalogueJson = ReplayCatalog.Serialize(new BundleCatalogDto
        {
            SchemaVersion = ReplayCatalog.SchemaVersion,
            GeneratedUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            Mode = ReplayCatalog.DemoMode,
            Sessions = [ReplayCatalog.Describe(Bundle.Manifest, "demo/7/", Bundle.Positions.Length)],
        });
        ServeBytes("bundles/catalog.json", CatalogueJson);
        ServeBytes(DemoFolder + BundleFiles.Manifest, Bundle.ManifestJson.ToArray());
        ServeBytes(DemoFolder + BundleFiles.Positions, Bundle.Positions.ToArray());
    }

    public ReplaySession Replay { get; }

    public ReplayBundle Bundle { get; }

    public byte[] CatalogueJson { get; }

    public IReadOnlyList<string> Requests => _requests;

    public HttpClient CreateClient() => new(this) { BaseAddress = new Uri("https://example.test/race-replay/") };

    public void ServeBytes(string path, byte[] body) =>
        _routes[path] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    public void ServeStatus(string path, HttpStatusCode status) => _routes[path] = () => new HttpResponseMessage(status);

    public void ServeFailure(string path, Exception failure) => _routes[path] = () => throw failure;

    public void ServeStream(string path, Func<Stream> body) =>
        _routes[path] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body()) };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = request.RequestUri!.AbsolutePath["/race-replay/".Length..];
        _requests.Add(path);
        return Task.FromResult(_routes.TryGetValue(path, out Func<HttpResponseMessage>? respond)
            ? respond()
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

/// <summary>Records progress synchronously, unlike <see cref="Progress{T}"/>, which posts to a synchronization context.</summary>
internal sealed class RecordingProgress : IProgress<double>
{
    public List<double> Values { get; } = [];

    public void Report(double value) => Values.Add(value);
}