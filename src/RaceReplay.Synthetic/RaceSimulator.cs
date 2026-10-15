namespace RaceReplay.Synthetic;

/// <summary>
/// The simulated race: each car's exact progress in metres at a fine time step, plus its lap and finish times.
/// Times are seconds relative to the race start; the simulation begins <c>PreRaceSeconds</c> before it.
/// </summary>
internal sealed class RaceSimulation
{
    public required double StartTime { get; init; }

    public required double TimeStep { get; init; }

    public required double LapMetres { get; init; }

    public required int RaceLaps { get; init; }

    /// <summary>Per car, progress in metres at each step. Negative before the start line (on the grid).</summary>
    public required double[][] Progress { get; init; }

    /// <summary>Per car, the times it completed each lap (crossed the line at k × lap length).</summary>
    public required double[][] LapCompletions { get; init; }

    /// <summary>Per car, when it took the chequered flag.</summary>
    public required double[] FinishTime { get; init; }

    /// <summary>Per car, when it leaves the track after its cool-down: the last moment it is observed.</summary>
    public required double[] EndTime { get; init; }

    /// <summary>Progress of <paramref name="car"/> at <paramref name="time"/>, interpolated between steps.</summary>
    public double ProgressAt(int car, double time)
    {
        double[] progress = Progress[car];
        double f = Math.Clamp((time - StartTime) / TimeStep, 0, progress.Length - 1);
        int i = Math.Min((int)f, progress.Length - 2);
        double a = f - i;
        return progress[i] + ((progress[i + 1] - progress[i]) * a);
    }
}

/// <summary>
/// A deliberately simple race model (docs/04 §11.2): per-car pace, lap-to-lap noise, a standing start from a
/// staggered grid, and a rule that a car can't pass the car ahead on the same lap unless it is clearly faster,
/// so trains form. Lapped cars yield (blue flags). Pit stops, safety cars and retirements arrive in v1.
/// </summary>
internal static class RaceSimulator
{
    public const double TimeStep = 0.1;

    private const double MaxTeamDeficit = 0.014;     // slowest car about 1.4 % off the fastest
    private const double MaxDriverDeficit = 0.008;
    private const double LapNoise = 0.004;           // ≈ 0.35 s on a 90 s lap
    internal const double MaxLapNoise = 0.012;
    private const double PassMargin = 0.006;         // pace advantage needed to get past
    private const double MinGapSeconds = 0.35;       // closest a blocked car follows
    private const double MinGapMetres = 8;
    private const double FirstGridSlotMetres = 5;
    private const double GridSlotMetres = 8;
    private const double CoolDownPace = 0.5;

    public static RaceSimulation Run(
        SpeedProfile profile,
        int[] teamOfCar,
        int raceLaps,
        SyntheticRaceOptions options,
        Xoshiro256StarStar random)
    {
        int cars = teamOfCar.Length;
        double lap = profile.LapMetres;
        double[] pace = Pace(teamOfCar, random);
        double[] noise = new double[cars];
        for (int c = 0; c < cars; c++)
        {
            noise[c] = DrawNoise(random);
        }

        double[] d = Grid(pace, random);
        double[] v = new double[cars];
        bool[] finished = new bool[cars];
        double[] finishTime = new double[cars];
        double[] endTime = new double[cars];
        Array.Fill(finishTime, double.NaN);
        Array.Fill(endTime, double.NaN);
        int[] nextLap = new int[cars];
        Array.Fill(nextLap, 1);

        int estimatedSteps = (int)((options.PreRaceSeconds + (raceLaps * profile.LapTimeSeconds * 1.15) + options.CoolDownSeconds) / TimeStep);
        List<double>[] trajectory = new List<double>[cars];
        List<double>[] completions = new List<double>[cars];
        for (int c = 0; c < cars; c++)
        {
            trajectory[c] = new List<double>(estimatedSteps) { d[c] };
            completions[c] = new List<double>(raceLaps);
        }

        double flagTime = double.NaN;
        double start = -options.PreRaceSeconds;
        double timeLimit = (raceLaps * profile.LapTimeSeconds * 3) + 3_600;
        double[] proposed = new double[cars];
        int[] order = new int[cars];
        List<(double Time, int Car)> crossings = [];

        // Times come from the step index, not a running sum, so step i is exactly at start + i × TimeStep.
        for (int step = 0; ; step++)
        {
            double t = start + (step * TimeStep);
            if (AllDone(finished, endTime, t))
            {
                break;
            }

            if (t > timeLimit)
            {
                throw new InvalidOperationException("The simulated race did not finish.");
            }

            double next = start + ((step + 1) * TimeStep);
            for (int c = 0; c < cars; c++)
            {
                if (next <= 0 || (finished[c] && t >= endTime[c]))
                {
                    proposed[c] = d[c]; // on the grid before the start, or gone after the cool-down
                    continue;
                }

                double factor = finished[c] ? CoolDownPace : pace[c] * noise[c];
                double target = profile.At(d[c]) * factor;
                double speed = target > v[c] ? Math.Min(target, v[c] + (SpeedProfile.Acceleration * TimeStep)) : target;
                proposed[c] = d[c] + (speed * TimeStep);
            }

            if (next > 0)
            {
                Block(d, proposed, finished, pace, noise, lap, order);
            }

            // Lap completions, processed in time order so the chequered flag is seen by every car that follows it.
            crossings.Clear();
            for (int c = 0; c < cars; c++)
            {
                for (int k = nextLap[c]; !finished[c] && proposed[c] >= k * lap; k++)
                {
                    crossings.Add((t + (TimeStep * ((k * lap) - d[c]) / (proposed[c] - d[c])), c));
                }
            }

            crossings.Sort();
            foreach ((double time, int c) in crossings)
            {
                if (finished[c])
                {
                    continue;
                }

                completions[c].Add(time);
                nextLap[c]++;
                if (double.IsNaN(flagTime) && completions[c].Count == raceLaps)
                {
                    flagTime = time;
                }

                if (!double.IsNaN(flagTime))
                {
                    finished[c] = true;
                    finishTime[c] = time;
                    endTime[c] = time + options.CoolDownSeconds;
                }
                else
                {
                    noise[c] = DrawNoise(random);
                }
            }

            for (int c = 0; c < cars; c++)
            {
                v[c] = (proposed[c] - d[c]) / TimeStep;
                d[c] = proposed[c];
                trajectory[c].Add(d[c]);
            }
        }

        return new RaceSimulation
        {
            StartTime = start,
            TimeStep = TimeStep,
            LapMetres = lap,
            RaceLaps = raceLaps,
            Progress = [.. trajectory.Select(list => list.ToArray())],
            LapCompletions = [.. completions.Select(list => list.ToArray())],
            FinishTime = finishTime,
            EndTime = endTime,
        };
    }

    // Team car performance times driver skill; team-mates share the car factor.
    private static double[] Pace(int[] teamOfCar, Xoshiro256StarStar random)
    {
        int teams = teamOfCar.Max() + 1;
        double[] teamFactor = new double[teams];
        for (int team = 0; team < teams; team++)
        {
            teamFactor[team] = 1 - random.NextDouble(0, MaxTeamDeficit);
        }

        double[] pace = new double[teamOfCar.Length];
        for (int c = 0; c < pace.Length; c++)
        {
            pace[c] = teamFactor[teamOfCar[c]] * (1 - random.NextDouble(0, MaxDriverDeficit));
        }

        return pace;
    }

    // Grid order follows pace with a little qualifying noise; slots are staggered behind the start line.
    private static double[] Grid(double[] pace, Xoshiro256StarStar random)
    {
        int cars = pace.Length;
        double[] qualifying = new double[cars];
        for (int c = 0; c < cars; c++)
        {
            qualifying[c] = pace[c] * (1 + (0.002 * random.NextGaussian()));
        }

        int[] slots = [.. Enumerable.Range(0, cars).OrderByDescending(c => qualifying[c]).ThenBy(c => c)];
        double[] d = new double[cars];
        for (int slot = 0; slot < cars; slot++)
        {
            d[slots[slot]] = -(FirstGridSlotMetres + (slot * GridSlotMetres));
        }

        return d;
    }

    private static double DrawNoise(Xoshiro256StarStar random) =>
        1 + Math.Clamp(random.NextGaussian() * LapNoise, -MaxLapNoise, MaxLapNoise);

    // A car running close behind another on the same lap stays behind unless it is clearly faster.
    private static void Block(double[] d, double[] proposed, bool[] finished, double[] pace, double[] noise, double lap, int[] order)
    {
        int racing = 0;
        for (int c = 0; c < d.Length; c++)
        {
            if (!finished[c])
            {
                order[racing++] = c;
            }
        }

        Span<int> running = order.AsSpan(0, racing);
        running.Sort((a, b) => d[b] != d[a] ? d[b].CompareTo(d[a]) : a.CompareTo(b));
        for (int k = 1; k < running.Length; k++)
        {
            int ahead = running[k - 1];
            int behind = running[k];
            if (d[ahead] - d[behind] >= lap / 2)
            {
                continue; // a lap apart: lapped traffic yields
            }

            double aheadSpeed = (proposed[ahead] - d[ahead]) / TimeStep;
            double gap = Math.Max(MinGapMetres, aheadSpeed * MinGapSeconds);
            bool clearlyFaster = pace[behind] * noise[behind] > pace[ahead] * noise[ahead] * (1 + PassMargin);
            if (proposed[ahead] - proposed[behind] < gap && !clearlyFaster)
            {
                proposed[behind] = Math.Max(d[behind], proposed[ahead] - gap);
            }
        }
    }

    private static bool AllDone(bool[] finished, double[] endTime, double t)
    {
        for (int c = 0; c < finished.Length; c++)
        {
            if (!finished[c] || t < endTime[c])
            {
                return false;
            }
        }

        return true;
    }
}