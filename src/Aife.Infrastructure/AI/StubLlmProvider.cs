using Aife.Application.AI;

namespace Aife.Infrastructure.AI;

/// <summary>
/// A simple LLM provider for MVP development and testing. Returns a canned
/// response. Real provider clients (Azure OpenAI, secondary) replace this in
/// the composition root when API keys are configured.
/// </summary>
public sealed class StubLlmProvider : ILlmProvider
{
    private readonly Func<LlmRequest, string> _responseFactory;

    public StubLlmProvider(string name = "stub", int priority = 100, string cannedResponse = "{}")
        : this(name, priority, _ => cannedResponse)
    {
    }

    public StubLlmProvider(string name, int priority, Func<LlmRequest, string> responseFactory)
    {
        Info = new LlmProviderInfo
        {
            Name = name,
            Priority = priority,
            Capabilities = new List<string> { "text", "json" }
        };
        _responseFactory = responseFactory;
    }

    public LlmProviderInfo Info { get; }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var response = new LlmResponse
        {
            Text = _responseFactory(request),
            Usage = new LlmUsage
            {
                PromptTokens = request.SystemMessage.Length / 4,
                CompletionTokens = _responseFactory(request).Length / 4,
                TotalTokens = (request.SystemMessage.Length + _responseFactory(request).Length) / 4
            },
            FinishReason = "stop"
        };

        return Task.FromResult(response);
    }
}
