namespace Aife.Application.AI;

/// <summary>
/// Token usage metadata returned by an LLM provider.
/// </summary>
public sealed record LlmUsage
{
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens { get; init; }
}
