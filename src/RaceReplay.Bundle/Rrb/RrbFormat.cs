using System.Buffers.Binary;

namespace RaceReplay.Bundle.Rrb;

/// <summary>Constants of the <c>.rrb</c> binary container, schema 1.0 (docs/04 §7.3). Everything is little-endian.</summary>
internal static class RrbFormat
{
    public const int HeaderSize = 64;
    public const int EntrySize = 16;
    public const int BlockAlignment = 8;
    public const ushort Major = 1;
    public const ushort Minor = 0;

    /// <summary>Limits that keep a hostile file from asking for absurd allocations (docs/04 §7.5).</summary>
    public const int MaxCars = 40;
    public const int MaxSamples = 2_000_000;
    public const int MaxChannelsPerCar = 8;

    public static ReadOnlySpan<byte> Magic => "RRB1"u8;
}

internal enum RrbKind : ushort
{
    Positions = 1,
    Gaps = 2,
}

internal enum RrbChannel : ushort
{
    X = 1,
    Y = 2,
    Progress = 3,
    Validity = 4,
    Gap = 10,
}

internal enum RrbEncoding : ushort
{
    Int16Quantised = 1,
    Float32 = 2,
    Bitmap = 3,
}

/// <summary>The 64-byte header.</summary>
internal readonly record struct RrbHeader(
    RrbKind Kind,
    int CarCount,
    int SampleCount,
    double SampleRateHz,
    float Scale,
    float OriginX,
    float OriginY,
    int DirectoryOffset,
    int EntryCount)
{
    public void Write(Span<byte> destination)
    {
        destination[..RrbFormat.HeaderSize].Clear();
        RrbFormat.Magic.CopyTo(destination);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], RrbFormat.Major);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], RrbFormat.Minor);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[8..], (ushort)Kind);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[10..], checked((ushort)CarCount));
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], checked((uint)SampleCount));
        BinaryPrimitives.WriteDoubleLittleEndian(destination[16..], SampleRateHz);
        BinaryPrimitives.WriteSingleLittleEndian(destination[24..], Scale);
        BinaryPrimitives.WriteSingleLittleEndian(destination[28..], OriginX);
        BinaryPrimitives.WriteSingleLittleEndian(destination[32..], OriginY);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[36..], 0); // flags: none in 1.0
        BinaryPrimitives.WriteUInt32LittleEndian(destination[40..], checked((uint)DirectoryOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(destination[44..], checked((uint)EntryCount));
    }

    /// <exception cref="BundleFormatException">The header is truncated, foreign, from a newer major version, or absurd.</exception>
    public static RrbHeader Parse(ReadOnlySpan<byte> data, RrbKind expectedKind)
    {
        if (data.Length < RrbFormat.HeaderSize || !data[..4].SequenceEqual(RrbFormat.Magic))
        {
            throw new BundleFormatException("Not a Race Replay binary file (missing RRB1 header).");
        }

        ushort major = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        if (major != RrbFormat.Major)
        {
            throw new BundleFormatException($"Binary format {major}.x is not supported; this app reads {RrbFormat.Major}.x.")
            {
                IsNewerVersion = major > RrbFormat.Major,
            };
        }

        RrbKind kind = (RrbKind)BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        if (kind != expectedKind)
        {
            throw new BundleFormatException($"Expected a {expectedKind} file but found kind {(ushort)kind}.");
        }

        int carCount = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
        uint sampleCount = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
        double rate = BinaryPrimitives.ReadDoubleLittleEndian(data[16..]);
        float scale = BinaryPrimitives.ReadSingleLittleEndian(data[24..]);
        float originX = BinaryPrimitives.ReadSingleLittleEndian(data[28..]);
        float originY = BinaryPrimitives.ReadSingleLittleEndian(data[32..]);
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(data[36..]);
        uint directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[40..]);
        uint entryCount = BinaryPrimitives.ReadUInt32LittleEndian(data[44..]);

        if (carCount > RrbFormat.MaxCars || sampleCount is 0 or > RrbFormat.MaxSamples)
        {
            throw new BundleFormatException($"Implausible size: {carCount} cars, {sampleCount} samples.");
        }

        if (!double.IsFinite(rate) || rate <= 0 || !float.IsFinite(scale) || scale <= 0
            || !float.IsFinite(originX) || !float.IsFinite(originY))
        {
            throw new BundleFormatException("The header's sample rate or quantisation parameters are invalid.");
        }

        if (flags != 0)
        {
            throw new BundleFormatException($"Unsupported encoding flags 0x{flags:X}.");
        }

        if (entryCount > (uint)(carCount * RrbFormat.MaxChannelsPerCar)
            || directoryOffset < RrbFormat.HeaderSize
            || directoryOffset + ((ulong)entryCount * RrbFormat.EntrySize) > (ulong)data.Length)
        {
            throw new BundleFormatException("The channel directory lies outside the file.");
        }

        return new RrbHeader(kind, carCount, (int)sampleCount, rate, scale, originX, originY, (int)directoryOffset, (int)entryCount);
    }
}

/// <summary>A 16-byte directory entry locating one channel of one car.</summary>
internal readonly record struct RrbEntry(int Driver, RrbChannel Channel, RrbEncoding Encoding, int Offset, int Length)
{
    public void Write(Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination, checked((ushort)Driver));
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], (ushort)Channel);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], (ushort)Encoding);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], checked((uint)Offset));
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], checked((uint)Length));
    }

    public static RrbEntry Parse(ReadOnlySpan<byte> data, int fileLength)
    {
        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
        if ((ulong)offset + length > (ulong)fileLength)
        {
            throw new BundleFormatException("A channel block lies outside the file.");
        }

        return new RrbEntry(
            BinaryPrimitives.ReadUInt16LittleEndian(data),
            (RrbChannel)BinaryPrimitives.ReadUInt16LittleEndian(data[2..]),
            (RrbEncoding)BinaryPrimitives.ReadUInt16LittleEndian(data[4..]),
            (int)offset,
            (int)length);
    }

    /// <summary>The block length this channel needs for <paramref name="samples"/> samples.</summary>
    public static int BlockLength(RrbEncoding encoding, int samples) => encoding switch
    {
        RrbEncoding.Int16Quantised => checked(samples * 2),
        RrbEncoding.Float32 => checked(samples * 4),
        RrbEncoding.Bitmap => (samples + 7) / 8,
        _ => throw new BundleFormatException($"Unknown channel encoding {(ushort)encoding}."),
    };
}