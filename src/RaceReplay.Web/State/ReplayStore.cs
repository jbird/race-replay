namespace RaceReplay.Web.State;

/// <summary>
/// The snapshot components render from (docs/05 §8). A value type, so publishing it from the render loop allocates
/// nothing. The timing tower, focus and chart mode join it in RR-061 and RR-063.
/// </summary>
public readonly record struct ReplayViewState(double Position, double Duration, bool IsPlaying, double Rate);

/// <summary>View state with one-way data flow: the render loop publishes, components subscribe (docs/05 §8, ADR-0009).</summary>
public sealed class ReplayStore
{
    public ReplayViewState State { get; private set; }

    /// <summary>Raised after every publish. Handlers re-render via <c>InvokeAsync(StateHasChanged)</c>.</summary>
    public event Action? Changed;

    public void Publish(ReplayViewState state)
    {
        State = state;
        Changed?.Invoke();
    }
}