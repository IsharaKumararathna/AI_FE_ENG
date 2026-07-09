namespace Aife.Domain.Generation;

/// <summary>
/// A framework-neutral tree of UI nodes produced from mappings or emitted by the
/// Prototype Generator (ADR-006). Consumed by the React Generator.
/// </summary>
public sealed class IntermediateUiTree
{
    public required string Page { get; init; }
    public required string Layout { get; init; }
    public IDictionary<string, string>? Tokens { get; init; }
    public IList<UiNode> Children { get; init; } = new List<UiNode>();
}
