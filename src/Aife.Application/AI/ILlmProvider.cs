namespace Aife.Application.AI;

/// <summary>
/// Abstraction for one LLM backend. Implementations live in
/// <c>Aife.Infrastructure</c> (ADR-002). AI stages never call providers
/// directly; they call <see cref="LlmRouter" />.
/// </summary>
public interface ILlmProvider
{
    LlmProviderInfo Info { get; }

    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct);
}
