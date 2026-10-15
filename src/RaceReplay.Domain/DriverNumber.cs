using System.Globalization;

namespace RaceReplay.Domain;

/// <summary>A car's race number, which identifies the driver within a session.</summary>
public readonly record struct DriverNumber(int Value)
{
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}