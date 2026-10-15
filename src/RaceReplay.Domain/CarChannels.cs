namespace RaceReplay.Domain;

/// <summary>
/// One car's channels on the timeline grid (docs/03 §7.2). Where <see cref="Valid"/> is false the values carry no
/// meaning, so readers must check it. <see cref="Progress"/> is empty until race progress is derived (M2).
/// </summary>
public sealed class CarChannels
{
    public CarChannels(
        DriverNumber driver,
        ReadOnlyMemory<float> x,
        ReadOnlyMemory<float> y,
        ValidityMask valid,
        ReadOnlyMemory<float> progress = default)
    {
        ArgumentNullException.ThrowIfNull(valid);
        if (x.Length != valid.Count || y.Length != valid.Count)
        {
            throw new ArgumentException(
                $"Car {driver}: X ({x.Length}) and Y ({y.Length}) must have one value per sample ({valid.Count}).");
        }

        if (!progress.IsEmpty && progress.Length != valid.Count)
        {
            throw new ArgumentException(
                $"Car {driver}: progress ({progress.Length}) must be empty or have one value per sample ({valid.Count}).",
                nameof(progress));
        }

        Driver = driver;
        X = x;
        Y = y;
        Valid = valid;
        Progress = progress;
    }

    public DriverNumber Driver { get; }

    public ReadOnlyMemory<float> X { get; }

    public ReadOnlyMemory<float> Y { get; }

    /// <summary>Monotonic race progress in track units, or empty if not derived.</summary>
    public ReadOnlyMemory<float> Progress { get; }

    public ValidityMask Valid { get; }

    public int SampleCount => Valid.Count;

    public bool HasProgress => !Progress.IsEmpty;
}