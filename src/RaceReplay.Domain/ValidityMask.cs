using System.Numerics;

namespace RaceReplay.Domain;

/// <summary>
/// One bit per timeline sample saying whether a car's channels hold observed data there. Bit <c>k</c> is sample
/// <c>k</c>, least significant bit first within each byte: the bundle's bitmap encoding (docs/04 §7.3).
/// </summary>
public sealed class ValidityMask
{
    private readonly ReadOnlyMemory<byte> _bits;

    /// <param name="bits">The bitmap; exactly <see cref="ByteLength"/>(<paramref name="count"/>) bytes.</param>
    /// <param name="count">Number of samples.</param>
    public ValidityMask(ReadOnlyMemory<byte> bits, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (bits.Length != ByteLength(count))
        {
            throw new ArgumentException(
                $"A mask of {count} samples needs {ByteLength(count)} bytes, not {bits.Length}.", nameof(bits));
        }

        _bits = bits;
        Count = count;
    }

    /// <summary>Number of samples.</summary>
    public int Count { get; }

    /// <summary>The bitmap. Padding bits after <see cref="Count"/> carry no meaning.</summary>
    public ReadOnlyMemory<byte> Bits => _bits;

    public bool this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return (_bits.Span[index >> 3] & (1 << (index & 7))) != 0;
        }
    }

    /// <summary>Bytes needed for <paramref name="count"/> bits.</summary>
    public static int ByteLength(int count) => (int)(((long)count + 7) / 8);

    public static ValidityMask FromBools(ReadOnlySpan<bool> values)
    {
        byte[] bits = new byte[ByteLength(values.Length)];
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i])
            {
                bits[i >> 3] |= (byte)(1 << (i & 7));
            }
        }

        return new ValidityMask(bits, values.Length);
    }

    /// <summary>Number of valid samples, ignoring padding bits.</summary>
    public int CountValid()
    {
        ReadOnlySpan<byte> bits = _bits.Span;
        int fullBytes = Count >> 3;
        int valid = 0;
        for (int i = 0; i < fullBytes; i++)
        {
            valid += BitOperations.PopCount(bits[i]);
        }

        int remainder = Count & 7;
        if (remainder > 0)
        {
            valid += BitOperations.PopCount((uint)(bits[fullBytes] & ((1 << remainder) - 1)));
        }

        return valid;
    }
}