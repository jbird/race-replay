using RaceReplay.Application.Ports;
using RaceReplay.Domain;
using RaceReplay.TestKit.Contracts;

namespace RaceReplay.Synthetic.Tests;

/// <summary>The production synthetic adapter obeys the same contract as OpenF1 (docs/06 §6).</summary>
public sealed class SyntheticProviderContractTests : RawSessionProviderContract
{
    protected override SessionKey KnownSession => SessionKey.Synthetic(7);

    protected override SessionKey ForeignSession => new(9839);

    protected override IRawSessionProvider CreateSut() =>
        new SyntheticRawSessionProvider(new SyntheticRaceGenerator(new SyntheticRaceOptions { Laps = 3, DriverCount = 6 }));
}