namespace Aife.Domain.Providers;

/// <summary>
/// Configuration for one LLM provider: endpoint, model, parameters.
/// </summary>
public sealed record LlmProviderConfig
{
    public required string Name { get; init; }
    public required string Endpoint { get; init; }
    public required string Model { get; init; }
    public IDictionary<string, object?>? Parameters { get; init; }
}
