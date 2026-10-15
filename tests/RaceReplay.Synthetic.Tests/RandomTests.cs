namespace RaceReplay.Synthetic.Tests;

public sealed class Xoshiro256StarStarTests
{
    private const int Draws = 200_000;

    [Fact]
    public void NextUInt64_ReferenceState_MatchesPublishedOutputs()
    {
        // Worked from the reference algorithm (prng.di.unimi.it): state {1, 2, 3, 4}.
        Xoshiro256StarStar random = new(1, 2, 3, 4);

        Assert.Equal(11520UL, random.NextUInt64());
        Assert.Equal(0UL, random.NextUInt64());
        Assert.Equal(1509978240UL, random.NextUInt64());
    }

    [Fact]
    public void SplitMix64_SeedZero_MatchesPublishedOutputs()
    {
        ulong state = 0;

        Assert.Equal(0xE220A8397B1DCDAFUL, SplitMix64.Next(ref state));
        Assert.Equal(0x6E789E6AA1B965F4UL, SplitMix64.Next(ref state));
        Assert.Equal(0x06C45D188009454FUL, SplitMix64.Next(ref state));
    }

    [Fact]
    public void Constructor_AllZeroState_Throws() =>
        Assert.Throws<ArgumentException>(() => new Xoshiro256StarStar(0, 0, 0, 0));

    [Fact]
    public void FromSeed_SameSeed_SameSequence()
    {
        Xoshiro256StarStar a = Xoshiro256StarStar.FromSeed(42);
        Xoshiro256StarStar b = Xoshiro256StarStar.FromSeed(42);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
        }
    }

    [Fact]
    public void FromSeed_DifferentSeeds_DifferentSequences() =>
        Assert.NotEqual(Xoshiro256StarStar.FromSeed(1).NextUInt64(), Xoshiro256StarStar.FromSeed(2).NextUInt64());

    [Fact]
    public void NextDouble_ManyDraws_UniformInUnitInterval()
    {
        Xoshiro256StarStar random = Xoshiro256StarStar.FromSeed(7);
        double sum = 0;

        for (int i = 0; i < Draws; i++)
        {
            double x = random.NextDouble();
            Assert.InRange(x, 0, Math.BitDecrement(1.0));
            sum += x;
        }

        Assert.Equal(0.5, sum / Draws, 0.005);
    }

    [Fact]
    public void NextDouble_Range_StaysInside()
    {
        Xoshiro256StarStar random = Xoshiro256StarStar.FromSeed(7);

        for (int i = 0; i < 1_000; i++)
        {
            Assert.InRange(random.NextDouble(-3, 5), -3, 5);
        }
    }

    [Fact]
    public void NextInt_ManyDraws_EveryValueEquallyLikely()
    {
        const int Buckets = 10;
        Xoshiro256StarStar random = Xoshiro256StarStar.FromSeed(11);
        int[] counts = new int[Buckets];

        for (int i = 0; i < Draws; i++)
        {
            counts[random.NextInt(Buckets)]++;
        }

        // Expected 20,000 each; ±5 % is about seven standard deviations.
        Assert.All(counts, c => Assert.InRange(c, 19_000, 21_000));
    }

    [Fact]
    public void NextInt_RangeOfOne_AlwaysZero()
    {
        Xoshiro256StarStar random = Xoshiro256StarStar.FromSeed(3);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(0, random.NextInt(1));
        }
    }

    [Fact]
    public void NextInt_MinAndMax_StaysInsideAndReachesBothEnds()
    {
        Xoshiro256StarStar random = Xoshiro256StarStar.FromSeed(5);
        HashSet<int> seen = [];

        for (int i = 0; i < 1_000; i++)
        {
            int x = random.NextInt(-2, 3);
            Assert.InRange(x, -2, 2);
            seen.Add(x);
        }

        Assert.Equal(5, seen.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NextInt_NonPositiveRange_Throws(int maxExclusive) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Xoshiro256StarStar.FromSeed(1).NextInt(maxExclusive));

    [Fact]
    public void NextInt_EmptyMinMaxRange_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Xoshiro256StarStar.FromSeed(1).NextInt(3, 3));

    [Fact]
    public void NextGaussian_ManyDraws_StandardNormal()
    {
        Xoshiro256StarStar random = Xoshiro256StarStar.FromSeed(13);
        double sum = 0, sumSquares = 0;

        for (int i = 0; i < Draws; i++)
        {
            double x = random.NextGaussian();
            sum += x;
            sumSquares += x * x;
        }

        double mean = sum / Draws;
        Assert.Equal(0, mean, 0.01);
        Assert.Equal(1, Math.Sqrt((sumSquares / Draws) - (mean * mean)), 0.01);
    }

    [Fact]
    public void NextBool_Probability_MatchesFrequency()
    {
        Xoshiro256StarStar random = Xoshiro256StarStar.FromSeed(17);
        int trues = 0;

        for (int i = 0; i < Draws; i++)
        {
            trues += random.NextBool(0.3) ? 1 : 0;
        }

        Assert.Equal(0.3, (double)trues / Draws, 0.005);
    }

    [Fact]
    public void Shuffle_Items_IsAPermutation()
    {
        int[] items = [.. Enumerable.Range(0, 50)];

        Xoshiro256StarStar.FromSeed(19).Shuffle<int>(items);

        Assert.Equal(Enumerable.Range(0, 50), items.Order());
        Assert.NotEqual(Enumerable.Range(0, 50), items);
    }
}

public sealed class SyntheticRandomTests
{
    [Fact]
    public void For_SameSeedAndChannel_SameSequence() =>
        Assert.Equal(
            SyntheticRandom.For(7, RandomChannel.Track).NextUInt64(),
            SyntheticRandom.For(7, RandomChannel.Track).NextUInt64());

    [Fact]
    public void For_DifferentChannels_IndependentSequences()
    {
        ulong[] firsts = [.. Enum.GetValues<RandomChannel>().Select(c => SyntheticRandom.For(7, c).NextUInt64())];

        Assert.Equal(firsts.Length, firsts.Distinct().Count());
    }

    [Fact]
    public void For_NegativeSeed_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SyntheticRandom.For(-1, RandomChannel.Race));
}