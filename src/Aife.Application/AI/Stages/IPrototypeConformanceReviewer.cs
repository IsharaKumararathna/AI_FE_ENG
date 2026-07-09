using Aife.Domain.Generation;

namespace Aife.Application.AI.Stages;

/// <summary>
/// Scores an uploaded prototype against the Design System and a reference UI
/// baseline (ADR-005). Advisory and non-blocking in the MVP. Consumes the
/// <see cref="PrototypeAnalysis" /> and raw HTML/CSS.
/// </summary>
public interface IPrototypeConformanceReviewer
{
    Task<PrototypeConformanceReport> ReviewAsync(
        Prototype prototype,
        PrototypeAnalysis analysis,
        CancellationToken ct);
}
