using System.Globalization;

namespace RaceReplay.Domain;

/// <summary>A 24-bit colour, written as six hex digits (<c>3671C6</c>) as OpenF1 and the manifest do.</summary>
public readonly record struct RgbColour(byte R, byte G, byte B)
{
    /// <summary>Parses <c>RRGGBB</c>, optionally prefixed with <c>#</c>; case-insensitive.</summary>
    /// <exception cref="FormatException">The text isn't a six-digit hex colour.</exception>
    public static RgbColour FromHex(ReadOnlySpan<char> hex) =>
        TryFromHex(hex, out RgbColour colour)
            ? colour
            : throw new FormatException($"'{hex}' is not a six-digit hex colour.");

    /// <summary>Parses <c>RRGGBB</c>, optionally prefixed with <c>#</c>; case-insensitive.</summary>
    public static bool TryFromHex(ReadOnlySpan<char> hex, out RgbColour colour)
    {
        if (hex.StartsWith('#'))
        {
            hex = hex[1..];
        }

        if (hex.Length == 6
            && byte.TryParse(hex[0..2], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte r)
            && byte.TryParse(hex[2..4], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte g)
            && byte.TryParse(hex[4..6], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte b))
        {
            colour = new RgbColour(r, g, b);
            return true;
        }

        colour = default;
        return false;
    }

    /// <summary>Six upper-case hex digits, without a <c>#</c>.</summary>
    public string ToHex() => string.Create(CultureInfo.InvariantCulture, $"{R:X2}{G:X2}{B:X2}");

    public override string ToString() => ToHex();
}