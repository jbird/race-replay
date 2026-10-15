namespace RaceReplay.Cli;

/// <summary>Process exit codes, a convention shared with racesim (docs/04 §6).</summary>
public static class ExitCodes
{
    public const int Success = 0;

    /// <summary>Unexpected error: a bug or an I/O failure.</summary>
    public const int Error = 1;

    /// <summary>Invalid usage: the command line didn't parse or validate.</summary>
    public const int Usage = 2;

    /// <summary>Data error: session not found, not a race, insufficient or malformed data.</summary>
    public const int Data = 3;

    /// <summary>Validation failed.</summary>
    public const int Validation = 4;

    /// <summary>Cancelled with Ctrl+C.</summary>
    public const int Cancelled = 130;
}

/// <summary>Where commands write, so tests can capture it.</summary>
public sealed record CliConsole(TextWriter Out, TextWriter Error)
{
    public static CliConsole System { get; } = new(Console.Out, Console.Error);
}