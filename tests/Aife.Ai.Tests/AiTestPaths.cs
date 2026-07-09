namespace Aife.Ai.Tests;

internal static class AiTestPaths
{
    public static string Knowledge { get; } = FindKnowledge();

    private static string FindKnowledge()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "knowledge");
            if (Directory.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the knowledge/ directory.");
    }
}
