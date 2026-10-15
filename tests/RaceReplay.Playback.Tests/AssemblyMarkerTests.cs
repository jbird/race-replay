namespace RaceReplay.Playback.Tests;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void Marker_ResolvesToProductionAssembly() =>
        Assert.Equal("RaceReplay.Playback", typeof(AssemblyMarker).Assembly.GetName().Name);
}