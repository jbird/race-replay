using RaceReplay.Domain;

namespace RaceReplay.Synthetic;

/// <summary>
/// Fictional names for circuits, teams and drivers. Surnames are assembled from syllables so they don't read as
/// real drivers; nothing here refers to real people, teams, marks or liveries (docs/04 §11.2).
/// </summary>
internal static class NameLists
{
    private static readonly string[] s_circuitStems =
    [
        "Kestrel", "Heron", "Ashford", "Merlin", "Bracken", "Thornbury", "Larkspur", "Wrenfield", "Osprey", "Cinder",
        "Halden", "Marrow", "Fenwick", "Juniper", "Corrie", "Bramble", "Stonecrop", "Whinmoor", "Alder", "Quillon",
    ];

    private static readonly string[] s_circuitKinds = ["Park", "Ring", "Raceway", "Circuit", "Downs", "Motor Circuit"];

    private static readonly string[] s_teamStems =
    [
        "Arrowfield", "Northlight", "Corvid", "Halcyon", "Ironbark", "Vantage", "Solent", "Meridian", "Tamsin", "Lodestar",
        "Kingfisher", "Ember", "Pinnacle", "Tidewater", "Granite", "Skylark",
    ];

    private static readonly string[] s_teamKinds = ["Racing", "Motorsport", "GP", "Racing Team", "Engineering"];

    private static readonly string[] s_givenNames =
    [
        "Ari", "Bram", "Cato", "Dara", "Elio", "Fenna", "Gil", "Hana", "Ivo", "Jory", "Kael", "Lune", "Mika", "Nils",
        "Oren", "Pia", "Quin", "Rhea", "Sami", "Tove", "Uma", "Vito", "Wren", "Yara", "Zeno", "Ines", "Teo", "Noor",
    ];

    private static readonly string[] s_surnameStarts =
    [
        "Bel", "Cor", "Dra", "Esk", "Fal", "Gor", "Hal", "Isk", "Jar", "Kel", "Lor", "Mor", "Nar", "Orv", "Pel", "Quor",
        "Ros", "Sel", "Tor", "Ulv", "Vor", "Wex", "Yst", "Zar",
    ];

    private static readonly string[] s_surnameEnds =
    [
        "anne", "berg", "dane", "ford", "gard", "holm", "ira", "kett", "lund", "mont", "nis", "ova", "rell", "sen",
        "tine", "vall", "wick", "zio",
    ];

    public static (string Name, string MeetingName) Circuit(Xoshiro256StarStar random)
    {
        string stem = Pick(random, s_circuitStems);
        return ($"{stem} {Pick(random, s_circuitKinds)}", $"{stem} Grand Prix");
    }

    /// <summary><paramref name="count"/> distinct team names.</summary>
    public static string[] Teams(Xoshiro256StarStar random, int count)
    {
        string[] stems = [.. s_teamStems];
        random.Shuffle<string>(stems);
        string[] names = new string[count];
        for (int i = 0; i < count; i++)
        {
            names[i] = $"{stems[i]} {Pick(random, s_teamKinds)}";
        }

        return names;
    }

    /// <summary>A driver's full name, with the surname in capitals as timing shows it, and its three-letter acronym.</summary>
    public static (string FullName, string Acronym) Driver(Xoshiro256StarStar random, ISet<string> usedAcronyms)
    {
        while (true)
        {
            string surname = Pick(random, s_surnameStarts) + Pick(random, s_surnameEnds);
            string acronym = surname[..3].ToUpperInvariant();
            if (usedAcronyms.Add(acronym))
            {
                return ($"{Pick(random, s_givenNames)} {surname.ToUpperInvariant()}", acronym);
            }
        }
    }

    public static int MaxTeams => s_teamStems.Length;

    private static string Pick(Xoshiro256StarStar random, string[] options) => options[random.NextInt(options.Length)];
}

/// <summary>Team colours: hues spread evenly around the wheel, at a lightness readable on light and dark maps.</summary>
internal static class TeamColours
{
    public static RgbColour[] Generate(Xoshiro256StarStar random, int count)
    {
        double offset = random.NextDouble(0, 360);
        RgbColour[] colours = new RgbColour[count];
        for (int i = 0; i < count; i++)
        {
            double hue = (offset + (360.0 * i / count)) % 360;
            double lightness = i % 2 == 0 ? 0.48 : 0.58; // alternate, so neighbouring hues differ in lightness too
            colours[i] = FromHsl(hue, 0.72, lightness);
        }

        random.Shuffle<RgbColour>(colours);
        return colours;
    }

    internal static RgbColour FromHsl(double hue, double saturation, double lightness)
    {
        double chroma = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
        double h = hue / 60;
        double x = chroma * (1 - Math.Abs((h % 2) - 1));
        (double r, double g, double b) = h switch
        {
            < 1 => (chroma, x, 0.0),
            < 2 => (x, chroma, 0.0),
            < 3 => (0.0, chroma, x),
            < 4 => (0.0, x, chroma),
            < 5 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };
        double m = lightness - (chroma / 2);
        return new RgbColour(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static byte ToByte(double channel) => (byte)Math.Round(Math.Clamp(channel, 0, 1) * 255, MidpointRounding.ToEven);
}