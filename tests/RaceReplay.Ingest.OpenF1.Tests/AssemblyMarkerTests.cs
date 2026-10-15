namespace RaceReplay.Ingest.OpenF1.Tests;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void Marker_ResolvesToProductionAssembly() =>
        Assert.Equal("RaceReplay.Ingest.OpenF1", typeof(AssemblyMarker).Assembly.GetName().Name);
}