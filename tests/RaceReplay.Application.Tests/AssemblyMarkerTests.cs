namespace RaceReplay.Application.Tests;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void Marker_ResolvesToProductionAssembly() =>
        Assert.Equal("RaceReplay.Application", typeof(AssemblyMarker).Assembly.GetName().Name);
}