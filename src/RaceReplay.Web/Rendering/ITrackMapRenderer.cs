namespace RaceReplay.Web.Rendering;

/// <summary>Draws the track map. It is the JS boundary, so tests substitute it (ADR-0009, docs/02 §6).</summary>
public interface ITrackMapRenderer
{
    /// <summary>Creates the canvases inside the element with id <paramref name="hostElementId"/> and draws the static layer.</summary>
    void Initialise(string hostElementId, TrackMapScene scene);

    /// <summary>
    /// Draws the cars from a <see cref="Playback.FrameSampler"/> frame, and keeps a copy to repaint after a resize: the
    /// loop only sends frames when the position changes. The frame is only valid during the call (docs/05 §5.2).
    /// </summary>
    void DrawFrame(Span<double> frame, int stride);

    /// <summary>Removes the canvases and stops watching for resizes.</summary>
    void Clear();
}