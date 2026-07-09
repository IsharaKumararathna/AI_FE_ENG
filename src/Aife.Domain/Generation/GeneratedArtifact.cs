namespace Aife.Domain.Generation;

/// <summary>
/// A generated React or TypeScript file.
/// </summary>
public sealed record GeneratedArtifact
{
    public required string Path { get; init; }
    public required string Content { get; init; }
}
