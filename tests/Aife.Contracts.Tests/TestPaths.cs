namespace Aife.Contracts.Tests;

/// <summary>
/// Locates the repository root at test time so tests can load schema and
/// knowledge files from disk without copying them to the output directory.
/// </summary>
internal static class TestPaths
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string Schemas => Path.Combine(RepoRoot, "schemas");

    public static string Knowledge => Path.Combine(RepoRoot, "knowledge");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "schemas")) &&
                Directory.Exists(Path.Combine(dir.FullName, "knowledge")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root containing schemas/ and knowledge/ directories.");
    }
}
