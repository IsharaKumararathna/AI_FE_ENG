using Aife.Domain.Generation;

namespace Aife.Application.AI.Stages;

/// <summary>
/// The result of generating a prototype from intent: the conformant Prototype
/// plus the IntermediateUiTree the generator used (ADR-006).
/// </summary>
public sealed record GeneratedPrototype
{
    public required Prototype Prototype { get; init; }
    public required IntermediateUiTree Tree { get; init; }
    public IList<string> TokensUsed { get; init; } = new List<string>();
    public IList<string> ComponentsUsed { get; init; } = new List<string>();
}
