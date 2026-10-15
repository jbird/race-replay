namespace OpenF1.Client.Tests;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void Marker_ResolvesToProductionAssembly() =>
        Assert.Equal("OpenF1.Client", typeof(AssemblyMarker).Assembly.GetName().Name);
}