namespace Aife.Application.AI;

/// <summary>
/// Metadata about a registered LLM provider, used by the router for selection.
/// </summary>
public sealed record LlmProviderInfo
{
    public required string Name { get; init; }
    public int Priority { get; init; }
    public IList<string> Capabilities { get; init; } = new List<string>();
    public bool IsAvailable { get; init; } = true;
}
