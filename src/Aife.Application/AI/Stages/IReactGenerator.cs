using Aife.Domain.Generation;

namespace Aife.Application.AI.Stages;

/// <summary>
/// Produces React and TypeScript from the Intermediate UI Tree using only
/// approved components and design tokens.
/// </summary>
public interface IReactGenerator
{
    Task<IReadOnlyList<GeneratedArtifact>> GenerateAsync(IntermediateUiTree tree, CancellationToken ct);
}
