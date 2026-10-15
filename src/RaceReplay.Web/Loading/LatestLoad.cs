using RaceReplay.Domain;

namespace RaceReplay.Web.Loading;

/// <summary>A finished load: the replay, or why it couldn't be loaded.</summary>
public sealed record LoadOutcome(ReplaySession? Replay, ReplayLoadException? Error);

/// <summary>
/// Runs a page's replay loads one at a time. Starting a load cancels the previous one, and the outcome of a load
/// that was superseded or cancelled, success or failure, is dropped rather than thrown.
/// </summary>
public sealed class LatestLoad : IDisposable
{
    private CancellationTokenSource? _current;

    /// <param name="load">Loads the replay; the token is cancelled when this load is superseded or disposed.</param>
    /// <returns>The outcome, or <see langword="null"/> when the load was superseded or cancelled.</returns>
    public async Task<LoadOutcome?> RunAsync(Func<CancellationToken, Task<ReplaySession>> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        Cancel();
        CancellationTokenSource current = _current = new CancellationTokenSource();
        try
        {
            ReplaySession replay = await load(current.Token);
            return current.IsCancellationRequested ? null : new LoadOutcome(replay, null);
        }
        catch (ReplayLoadException e)
        {
            return current.IsCancellationRequested ? null : new LoadOutcome(null, e);
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Cancels the running load, if any.</summary>
    public void Cancel()
    {
        _current?.Cancel();
        _current?.Dispose();
        _current = null;
    }

    public void Dispose() => Cancel();
}