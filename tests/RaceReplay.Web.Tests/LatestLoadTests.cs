using RaceReplay.Domain;
using RaceReplay.TestKit;
using RaceReplay.Web.Loading;

namespace RaceReplay.Web.Tests;

public sealed class LatestLoadTests : IDisposable
{
    private readonly LatestLoad _loads = new();
    private readonly ReplaySession _replay = TestReplays.Small();

    public void Dispose() => _loads.Dispose();

    [Fact]
    public async Task Run_Succeeds_ReturnsTheReplay()
    {
        LoadOutcome? outcome = await _loads.RunAsync(_ => Task.FromResult(_replay));

        Assert.Same(_replay, outcome?.Replay);
        Assert.Null(outcome?.Error);
    }

    [Fact]
    public async Task Run_Fails_ReturnsTheError()
    {
        ReplayLoadException error = new("gone") { Failure = ReplayLoadFailure.Unavailable };

        LoadOutcome? outcome = await _loads.RunAsync(_ => Task.FromException<ReplaySession>(error));

        Assert.Same(error, outcome?.Error);
        Assert.Null(outcome?.Replay);
    }

    [Fact]
    public async Task Run_SupersededLoadFailsLater_ReturnsNullWithoutThrowing()
    {
        TaskCompletionSource<ReplaySession> first = new();
        CancellationToken firstToken = default;
        Task<LoadOutcome?> superseded = _loads.RunAsync(token =>
        {
            firstToken = token;
            return first.Task;
        });

        LoadOutcome? current = await _loads.RunAsync(_ => Task.FromResult(_replay));
        first.SetException(new ReplayLoadException("corrupt") { Failure = ReplayLoadFailure.Corrupt });

        Assert.True(firstToken.IsCancellationRequested);
        Assert.Null(await superseded);
        Assert.Same(_replay, current?.Replay);
    }

    [Fact]
    public async Task Run_SupersededLoadSucceedsLater_ReturnsNull()
    {
        TaskCompletionSource<ReplaySession> first = new();
        Task<LoadOutcome?> superseded = _loads.RunAsync(_ => first.Task);

        await _loads.RunAsync(_ => Task.FromResult(_replay));
        first.SetResult(_replay);

        Assert.Null(await superseded);
    }

    [Fact]
    public async Task Run_DisposedWhileLoading_ReturnsNullOnCancellation()
    {
        Task<LoadOutcome?> running = _loads.RunAsync(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return _replay;
        });

        _loads.Dispose();

        Assert.Null(await running);
    }

    [Fact]
    public async Task Run_CancelledLoadFailsWithANetworkError_ReturnsNull()
    {
        TaskCompletionSource<ReplaySession> first = new();
        Task<LoadOutcome?> running = _loads.RunAsync(_ => first.Task);

        _loads.Cancel();
        first.SetException(new ReplayLoadException("aborted") { Failure = ReplayLoadFailure.Unavailable });

        Assert.Null(await running);
    }
}