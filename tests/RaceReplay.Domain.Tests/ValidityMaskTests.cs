namespace RaceReplay.Domain.Tests;

public sealed class ValidityMaskTests
{
    [Fact]
    public void FromBools_Values_SetsBitsLeastSignificantFirst()
    {
        ValidityMask mask = ValidityMask.FromBools([true, false, true, false, false, false, false, false, true]);

        Assert.Equal(9, mask.Count);
        Assert.Equal([0b0000_0101, 0b0000_0001], mask.Bits.ToArray());
    }

    [Fact]
    public void Indexer_EachSample_ReadsItsBit()
    {
        bool[] values = [true, false, false, true, true, false, true, false, false, true, true];

        ValidityMask mask = ValidityMask.FromBools(values);

        for (int i = 0; i < values.Length; i++)
        {
            Assert.Equal(values[i], mask[i]);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Indexer_OutsideTheMask_Throws(int index)
    {
        ValidityMask mask = ValidityMask.FromBools([true, true, true]);

        Assert.Throws<ArgumentOutOfRangeException>(() => mask[index]);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(8, 1)]
    [InlineData(9, 2)]
    [InlineData(int.MaxValue, 268_435_456)]
    public void ByteLength_Count_RoundsUpToWholeBytes(int count, int expected) =>
        Assert.Equal(expected, ValidityMask.ByteLength(count));

    [Fact]
    public void Constructor_WrongByteCount_Throws() =>
        Assert.Throws<ArgumentException>(() => new ValidityMask(new byte[2], 17));

    [Fact]
    public void Constructor_NegativeCount_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ValidityMask(Array.Empty<byte>(), -1));

    [Fact]
    public void CountValid_PaddingBitsSet_IgnoresThem()
    {
        // 10 samples: the first byte is all valid; in the second only bit 1 is a sample bit that's set.
        // Bits 2–7 of the second byte are padding and must not be counted.
        ValidityMask mask = new(new byte[] { 0xFF, 0b1111_1110 }, 10);

        Assert.Equal(9, mask.CountValid());
    }

    [Fact]
    public void CountValid_WholeBytes_CountsEverySetBit()
    {
        ValidityMask mask = ValidityMask.FromBools([true, false, true, true, false, true, true, true]);

        Assert.Equal(6, mask.CountValid());
    }
}