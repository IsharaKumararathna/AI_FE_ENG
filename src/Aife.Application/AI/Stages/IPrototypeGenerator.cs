using Aife.Domain.Generation;

namespace Aife.Application.AI.Stages;

/// <summary>
/// Generates a DS-conformant prototype from a PO's intent spec (ADR-006).
/// Conformant by construction: only emits approved components, tokens, layouts,
/// and reference UI patterns from the Knowledge Base.
/// </summary>
public interface IPrototypeGenerator
{
    Task<GeneratedPrototype> GenerateAsync(PrototypeRequest request, CancellationToken ct);
}
