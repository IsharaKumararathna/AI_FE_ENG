using Polly;
using Polly.Retry;

namespace Aife.Application.AI;

/// <summary>
/// The only component AI stages call for LLM completions (ADR-002). Selects a
/// provider per request by capability, cost, and policy, then applies resilience
/// (retry with exponential backoff). Provider-specific quirks are isolated in
/// each <see cref="ILlmProvider" /> implementation.
/// </summary>
public sealed class LlmRouter
{
    private readonly IReadOnlyList<ILlmProvider> _providers;
    private readonly ResiliencePipeline _resiliencePipeline;

    public LlmRouter(IEnumerable<ILlmProvider> providers)
    {
        _providers = providers?.OrderBy(p => p.Info.Priority).ToList()
            ?? throw new ArgumentNullException(nameof(providers));

        if (_providers.Count == 0)
            throw new ArgumentException("At least one LLM provider must be registered.", nameof(providers));

        _resiliencePipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
            })
            .Build();
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var provider = SelectProvider(request.StageContext);

        var response = await _resiliencePipeline.ExecuteAsync(
            async token => await provider.CompleteAsync(request, token),
            ct);

        return response with { ProviderName = provider.Info.Name };
    }

    internal ILlmProvider SelectProvider(StageContext? context)
    {
        var available = _providers.Where(p => p.Info.IsAvailable).ToList();
        if (available.Count == 0)
            throw new InvalidOperationException("No available LLM provider.");

        if (context is null || context.RequiredCapabilities.Count == 0)
            return available[0];

        foreach (var provider in available)
        {
            if (context.RequiredCapabilities.All(cap =>
                provider.Info.Capabilities.Contains(cap, StringComparer.OrdinalIgnoreCase)))
            {
                return provider;
            }
        }

        // Fall back to the highest-priority provider if no capability match.
        return available[0];
    }
}
