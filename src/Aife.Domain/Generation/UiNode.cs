namespace Aife.Domain.Generation;

/// <summary>
/// One node in the intermediate tree: a component reference, props, and token bindings.
/// </summary>
public sealed record UiNode
{
    public string? NodeId { get; init; }
    public required string ComponentId { get; init; }
    public string? Variant { get; init; }
    public IDictionary<string, object?>? Props { get; init; }
    public IDictionary<string, string>? TokenBindings { get; init; }
    public string? Text { get; init; }
    public IList<UiNode> Children { get; init; } = new List<UiNode>();
}
