using RaceReplay.Domain;

namespace RaceReplay.Synthetic;

/// <summary>A fictional field: two drivers per team, team-mates sharing their team's colour (docs/04 §11.2).</summary>
internal static class FieldGenerator
{
    public static Driver[] Generate(Xoshiro256StarStar random, int driverCount)
    {
        int teamCount = driverCount / 2;
        string[] teams = NameLists.Teams(random, teamCount);
        RgbColour[] colours = TeamColours.Generate(random, teamCount);

        int[] numbers = [.. Enumerable.Range(2, 98)]; // 2–99; #1 is traditionally the champion's
        random.Shuffle<int>(numbers);

        HashSet<string> acronyms = [];
        Driver[] drivers = new Driver[driverCount];
        for (int i = 0; i < driverCount; i++)
        {
            (string fullName, string acronym) = NameLists.Driver(random, acronyms);
            int team = i / 2;
            drivers[i] = new Driver(new DriverNumber(numbers[i]), acronym, fullName, teams[team], colours[team]);
        }

        return drivers;
    }
}