using Aife.Domain.Generation;

namespace Aife.Application.AI.Stages;

/// <summary>
/// Maps detected elements to approved Design System components with confidence.
/// </summary>
public interface IComponentMapper
{
    Task<IReadOnlyList<ComponentMapping>> MapAsync(PrototypeAnalysis analysis, CancellationToken ct);
}
