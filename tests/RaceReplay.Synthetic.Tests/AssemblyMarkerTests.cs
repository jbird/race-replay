namespace RaceReplay.Synthetic.Tests;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void Marker_ResolvesToProductionAssembly() =>
        Assert.Equal("RaceReplay.Synthetic", typeof(AssemblyMarker).Assembly.GetName().Name);
}