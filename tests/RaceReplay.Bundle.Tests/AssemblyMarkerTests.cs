namespace RaceReplay.Bundle.Tests;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void Marker_ResolvesToProductionAssembly() =>
        Assert.Equal("RaceReplay.Bundle", typeof(AssemblyMarker).Assembly.GetName().Name);
}