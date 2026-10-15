using RaceReplay.Web.Rendering;

namespace RaceReplay.Web.Tests;

/// <summary>Stands in for the canvas renderer at the JS boundary. Copies frames into a buffer it owns, so it allocates nothing per frame.</summary>
internal sealed class FakeTrackMapRenderer : ITrackMapRenderer
{
    public string? HostElementId { get; private set; }

    public TrackMapScene? Scene { get; private set; }

    public int Initialised { get; private set; }

    public int Cleared { get; private set; }

    public int FramesDrawn { get; private set; }

    public double[] LastFrame { get; private set; } = [];

    public int LastStride { get; private set; }

    public void Initialise(string hostElementId, TrackMapScene scene)
    {
        HostElementId = hostElementId;
        Scene = scene;
        Initialised++;
    }

    public void DrawFrame(Span<double> frame, int stride)
    {
        if (LastFrame.Length != frame.Length)
        {
            LastFrame = new double[frame.Length];
        }

        frame.CopyTo(LastFrame);
        LastStride = stride;
        FramesDrawn++;
    }

    public void Clear() => Cleared++;
}

/// <summary>Stands in for <c>requestAnimationFrame</c>: tests call <see cref="Tick"/> to run a frame.</summary>
internal sealed class FakeFrameScheduler : IFrameScheduler
{
    public RenderLoop? Loop { get; private set; }

    public int Attached { get; private set; }

    public int Detached { get; private set; }

    public void Attach(RenderLoop renderLoop)
    {
        Loop = renderLoop;
        Attached++;
    }

    public void Detach()
    {
        Loop = null;
        Detached++;
    }

    public void Tick(double timestampMs) => Loop?.Tick(timestampMs);
}