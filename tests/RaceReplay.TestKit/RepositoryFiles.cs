namespace RaceReplay.TestKit;

/// <summary>Files checked into the repository, such as <c>config/</c>, for tests that guard them.</summary>
public static class RepositoryFiles
{
    /// <summary>The repository root: the nearest folder above the test binaries that holds <c>RaceReplay.slnx</c>.</summary>
    public static string Root()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RaceReplay.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"RaceReplay.slnx not found above {AppContext.BaseDirectory}.");
    }

    public static string PathTo(params string[] parts) => Path.Combine([Root(), .. parts]);
}