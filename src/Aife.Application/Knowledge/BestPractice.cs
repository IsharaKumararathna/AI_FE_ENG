namespace Aife.Application.Knowledge;

/// <summary>
/// One best-practice entry, sourced from Markdown with YAML frontmatter.
/// </summary>
public sealed record BestPractice
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public IList<string> Tags { get; init; } = new List<string>();
    public required string Content { get; init; }
}
