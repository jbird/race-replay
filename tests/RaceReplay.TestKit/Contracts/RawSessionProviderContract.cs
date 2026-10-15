using RaceReplay.Application.Ports;
using RaceReplay.Domain;

using Xunit;

namespace RaceReplay.TestKit.Contracts;

/// <summary>
/// The behaviour every <see cref="IRawSessionProvider"/> must have (docs/02 §6, docs/06 §6). Each implementation's test
/// project derives from this class, and xUnit runs the inherited facts against it.
/// </summary>
public abstract class RawSessionProviderContract
{
    protected abstract SessionKey KnownSession { get; }

    /// <summary>A key outside this provider's range.</summary>
    protected abstract SessionKey ForeignSession { get; }

    protected abstract IRawSessionProvider CreateSut();

    [Fact]
    public async Task Load_KnownSession_ReturnsStrictlyAscendingUtcSamplesPerDriver()
    {
        RawSessionData raw = await Load();

        Assert.NotEmpty(raw.Locations);
        foreach ((DriverNumber driver, IReadOnlyList<RawLocationSample> samples) in raw.Locations)
        {
            Assert.All(samples, s => Assert.Equal(TimeSpan.Zero, s.Timestamp.Offset));
            Assert.All(samples, s => Assert.Equal(driver, s.Driver));
            for (int i = 1; i < samples.Count; i++)
            {
                Assert.True(samples[i].Timestamp > samples[i - 1].Timestamp, $"driver {driver}, sample {i}");
            }
        }
    }

    [Fact]
    public async Task Load_KnownSession_DescribesTheRequestedSession()
    {
        RawSessionData raw = await Load();

        Assert.Equal(KnownSession, raw.Session.Key);
        Assert.Equal(TimeSpan.Zero, raw.Session.StartUtc.Offset);
    }

    [Fact]
    public async Task Load_KnownSession_DriverNumbersAreUniqueAndCoverTheLocations()
    {
        RawSessionData raw = await Load();

        HashSet<DriverNumber> drivers = [.. raw.Drivers.Select(d => d.Number)];
        Assert.Equal(raw.Drivers.Count, drivers.Count);
        Assert.All(raw.Locations.Keys, driver => Assert.Contains(driver, drivers));
        Assert.All(raw.Drivers, d => Assert.Matches("^[A-Z]{3}$", d.Acronym));
    }

    [Fact]
    public async Task Load_KnownSession_EachDriversLapsAreContiguousAndInOrder()
    {
        RawSessionData raw = await Load();

        HashSet<DriverNumber> finished = [];
        DriverNumber? current = null;
        int previousLap = 0;
        foreach (RawLap lap in raw.Laps)
        {
            if (lap.Driver != current)
            {
                Assert.True(current is null || finished.Add(current.Value), $"driver {current}'s laps are split up");
                Assert.DoesNotContain(lap.Driver, finished);
                current = lap.Driver;
                previousLap = 0;
            }

            Assert.True(lap.LapNumber > previousLap, $"driver {lap.Driver}: lap {lap.LapNumber} after {previousLap}");
            previousLap = lap.LapNumber;
        }
    }

    [Fact]
    public async Task Load_ForeignSession_ThrowsSessionNotFound() =>
        await Assert.ThrowsAsync<SessionNotFoundException>(
            () => CreateSut().LoadAsync(ForeignSession, TestContext.Current.CancellationToken));

    [Fact]
    public async Task Load_CancelledToken_Throws()
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateSut().LoadAsync(KnownSession, cancelled.Token));
    }

    private Task<RawSessionData> Load() => CreateSut().LoadAsync(KnownSession, TestContext.Current.CancellationToken);
}