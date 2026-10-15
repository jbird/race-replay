namespace RaceReplay.Analysis;

/// <summary>
/// The session's data can't support a replay: no drivers, no usable lap for a track outline, and so on.
/// The CLI reports it as a data error (exit code 3) rather than a bug.
/// </summary>
public sealed class InsufficientDataException : Exception
{
    public InsufficientDataException()
    {
    }

    public InsufficientDataException(string message)
        : base(message)
    {
    }

    public InsufficientDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}