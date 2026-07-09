using Aife.Domain.Generation;

namespace Aife.Application.Pipeline;

/// <summary>
/// The result of a generation session: the session state plus all stage outputs.
/// </summary>
public sealed record GenerationResult
{
    public required GenerationSession Session { get; init; }
    public PrototypeAnalysis? Analysis { get; init; }
    public IReadOnlyList<ComponentMapping>? Mappings { get; init; }
    public IntermediateUiTree? Tree { get; init; }
    public IReadOnlyList<GeneratedArtifact>? Artifacts { get; init; }
    public ReviewReport? Review { get; init; }
}
