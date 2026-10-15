using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

using RaceReplay.Web.Rendering;

namespace RaceReplay.Web.Interop;

/// <summary>
/// The JS → .NET edge of the frame loop (docs/05 §5.3). <c>[JSExport]</c> methods must be static, so this holds the
/// current loop in the app's only static mutable field, confined to the interop edge.
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class RenderLoopBridge
{
    private static RenderLoop? s_loop;

    public static void Attach(RenderLoop loop) => s_loop = loop;

    public static void Detach() => s_loop = null;

    /// <summary>Called by <c>renderLoop.js</c> on every animation frame.</summary>
    [JSExport]
    internal static void OnFrame(double timestampMs) => s_loop?.Tick(timestampMs);
}

/// <summary>Starts and stops <c>requestAnimationFrame</c> in <c>wwwroot/js/renderLoop.js</c>.</summary>
[SupportedOSPlatform("browser")]
internal static partial class RenderLoopInterop
{
    public const string Module = "renderLoop";

    [JSImport("start", Module)]
    internal static partial void Start();

    [JSImport("stop", Module)]
    internal static partial void Stop();
}

/// <summary>Drives the render loop from <c>requestAnimationFrame</c>.</summary>
[SupportedOSPlatform("browser")]
public sealed class AnimationFrameScheduler : IFrameScheduler
{
    public void Attach(RenderLoop renderLoop)
    {
        ArgumentNullException.ThrowIfNull(renderLoop);
        RenderLoopBridge.Attach(renderLoop);
        RenderLoopInterop.Start();
    }

    public void Detach()
    {
        RenderLoopInterop.Stop();
        RenderLoopBridge.Detach();
    }
}