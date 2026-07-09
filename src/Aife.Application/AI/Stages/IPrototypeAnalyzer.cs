using Aife.Domain.Generation;

namespace Aife.Application.AI.Stages;

/// <summary>
/// Detects layout regions and UI primitives in a prototype.
/// </summary>
public interface IPrototypeAnalyzer
{
    Task<PrototypeAnalysis> AnalyzeAsync(Prototype prototype, CancellationToken ct);
}
