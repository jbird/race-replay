namespace RaceReplay.Web.Rendering;

/// <summary>Calls the render loop once per display frame: <c>requestAnimationFrame</c> in the browser. A JS boundary, like <see cref="ITrackMapRenderer"/>.</summary>
public interface IFrameScheduler
{
    /// <summary>Starts calling <paramref name="renderLoop"/> every frame, replacing any loop attached before.</summary>
    void Attach(RenderLoop renderLoop);

    /// <summary>Stops calling the render loop.</summary>
    void Detach();
}