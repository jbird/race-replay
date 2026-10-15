using RaceReplay.Domain;
using RaceReplay.TestKit;
using RaceReplay.Web.Rendering;

namespace RaceReplay.Web.Tests;

public sealed class TrackMapSceneTests
{
    [Fact]
    public void For_Replay_FlattensTheCentrelineAndKeepsDriverOrder()
    {
        ReplaySession replay = TestReplays.Small(cars: 4);

        TrackMapScene scene = TrackMapScene.For(replay);

        TrackPoint[] points = replay.Track.Centreline.ToArray();
        Assert.Equal(points.Length * 2, scene.Centreline.Length);
        Assert.Equal(points[1].X, scene.Centreline[2]);
        Assert.Equal(points[1].Y, scene.Centreline[3]);
        Assert.Equal(replay.Track.DisplayRotationDegrees, scene.RotationDegrees);
        Assert.Equal(replay.Drivers.Select(d => d.Acronym), scene.Acronyms);
        Assert.Equal(replay.Drivers.Select(d => "#" + d.TeamColour.ToHex()), scene.Colours);
    }

    [Fact]
    public void For_TeamMates_DashesOnlyTheSecondCarsOutline()
    {
        // TestReplays puts drivers 0 and 1 in Team 0, and 2 and 3 in Team 1.
        TrackMapScene scene = TrackMapScene.For(TestReplays.Small(cars: 4));

        Assert.Equal([0, 1, 0, 1], scene.Dashed);
    }

    [Theory]
    [InlineData(255, 255, 255, TrackMapScene.DarkOutline)]
    [InlineData(255, 235, 59, TrackMapScene.DarkOutline)]
    [InlineData(0, 0, 0, TrackMapScene.LightOutline)]
    [InlineData(39, 33, 158, TrackMapScene.LightOutline)]
    public void OutlineFor_TeamColour_ContrastsWithIt(byte r, byte g, byte b, string expected) =>
        Assert.Equal(expected, TrackMapScene.OutlineFor(new RgbColour(r, g, b)));
}