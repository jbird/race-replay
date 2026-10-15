namespace RaceReplay.Domain.Tests;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void Marker_ResolvesToProductionAssembly() =>
        Assert.Equal("RaceReplay.Domain", typeof(AssemblyMarker).Assembly.GetName().Name);
}