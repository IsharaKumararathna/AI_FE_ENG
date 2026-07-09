namespace Aife.Domain.Prompting;

/// <summary>
/// An immutable, semver-tagged snapshot of a prompt template.
/// Edits create a new version; an existing version is never mutated.
/// </summary>
public sealed record PromptVersion
{
    public required string Key { get; init; }
    public required string Version { get; init; }
    public required string SystemMessage { get; init; }
    public IList<string> Variables { get; init; } = new List<string>();
    public required string OutputContract { get; init; }
    public DateTime PublishedAt { get; init; } = DateTime.UtcNow;
}
