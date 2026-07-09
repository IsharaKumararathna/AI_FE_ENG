using Aife.Domain.Generation;

namespace Aife.Application.AI.Stages;

/// <summary>
/// Scores generated output for compliance, accessibility, and architecture.
/// </summary>
public interface IAiReviewer
{
    Task<ReviewReport> ReviewAsync(
        IReadOnlyList<GeneratedArtifact> artifacts,
        IntermediateUiTree tree,
        CancellationToken ct);
}
