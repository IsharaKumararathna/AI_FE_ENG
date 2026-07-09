namespace Aife.Application.AI;

/// <summary>
/// A request to an LLM provider, sent via the <see cref="LlmRouter" />.
/// </summary>
public sealed record LlmRequest
{
    public required string SystemMessage { get; init; }
    public required string UserMessage { get; init; }
    public string? Model { get; init; }
    public bool JsonMode { get; init; }
    public IDictionary<string, object?>? Parameters { get; init; }
    public StageContext? StageContext { get; init; }
}
