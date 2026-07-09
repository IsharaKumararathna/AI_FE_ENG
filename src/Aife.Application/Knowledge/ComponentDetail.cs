namespace Aife.Application.Knowledge;

/// <summary>
/// Full component representation as stored in the Knowledge Base.
/// </summary>
public sealed record ComponentDetail
{
    public required string ComponentId { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Status { get; init; }
    public string? Description { get; init; }
    public IList<ComponentProp> Props { get; init; } = new List<ComponentProp>();
    public IList<ComponentVariant>? Variants { get; init; }
    public IList<string>? TokensConsumed { get; init; }
    public IList<ComponentExample>? Examples { get; init; }
    public ComponentAccessibility? Accessibility { get; init; }
    public IList<string>? MapsFromHtml { get; init; }
}
