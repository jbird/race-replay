using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

using RaceReplay.Web.Rendering;

namespace RaceReplay.Web.Interop;

/// <summary>Calls into <c>wwwroot/js/trackMap.js</c> (docs/05 §5.1).</summary>
[SupportedOSPlatform("browser")]
internal static partial class TrackMapInterop
{
    public const string Module = "trackMap";

    [JSImport("init", Module)]
    internal static partial void Init(
        string hostElementId,
        [JSMarshalAs<JSType.Array<JSType.Number>>] double[] centreline,
        double rotationDegrees,
        [JSMarshalAs<JSType.Array<JSType.String>>] string[] acronyms,
        [JSMarshalAs<JSType.Array<JSType.String>>] string[] colours,
        [JSMarshalAs<JSType.Array<JSType.String>>] string[] outlines,
        [JSMarshalAs<JSType.Array<JSType.Number>>] int[] dashed);

    /// <summary>Passes the frame as a view over WebAssembly memory, without copying (docs/05 §5.2).</summary>
    [JSImport("drawCars", Module)]
    internal static partial void DrawCars([JSMarshalAs<JSType.MemoryView>] Span<double> frame, int stride);

    [JSImport("clear", Module)]
    internal static partial void Clear();
}

/// <summary>Draws the track map on two stacked canvases (docs/05 §6).</summary>
[SupportedOSPlatform("browser")]
public sealed class CanvasTrackMapRenderer : ITrackMapRenderer
{
    public void Initialise(string hostElementId, TrackMapScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        TrackMapInterop.Init(
            hostElementId, scene.Centreline, scene.RotationDegrees, scene.Acronyms, scene.Colours, scene.Outlines, scene.Dashed);
    }

    public void DrawFrame(Span<double> frame, int stride) => TrackMapInterop.DrawCars(frame, stride);

    public void Clear() => TrackMapInterop.Clear();
}