namespace Aife.Knowledge.Tests;

/// <summary>
/// Locates the repository root at test time so the knowledge/ sample dataset
/// can be loaded by <see cref="JsonKnowledgeProvider" />.
/// </summary>
internal static class KnowledgeTestPaths
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
