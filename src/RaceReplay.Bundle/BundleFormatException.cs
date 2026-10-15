namespace RaceReplay.Bundle;

/// <summary>
/// A bundle or package is malformed, corrupt, or from an incompatible format version. Bundles are untrusted input,
/// so the reader turns every problem into this one exception type (docs/04 §7.5).
/// </summary>
public sealed class BundleFormatException : Exception
{
    public BundleFormatException()
    {
    }

    public BundleFormatException(string message)
        : base(message)
    {
    }

    public BundleFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>True when the data is from a newer major format version than this reader understands.</summary>
    public bool IsNewerVersion { get; init; }
}