namespace RaceReplay.Analysis.Tests;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void Marker_ResolvesToProductionAssembly() =>
        Assert.Equal("RaceReplay.Analysis", typeof(AssemblyMarker).Assembly.GetName().Name);
}