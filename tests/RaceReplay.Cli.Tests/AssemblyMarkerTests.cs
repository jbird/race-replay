namespace RaceReplay.Cli.Tests;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void Marker_ResolvesToProductionAssembly() =>
        Assert.Equal("race-replay", typeof(AssemblyMarker).Assembly.GetName().Name);
}