namespace Aife.Application.AI;

/// <summary>
/// The response from an LLM provider call.
/// </summary>
public sealed record LlmResponse
{
    public required string Text { get; init; }
    public LlmUsage? Usage { get; init; }
    public string? FinishReason { get; init; }
    public string? ProviderName { get; init; }
}
