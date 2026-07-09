using Aife.Application.AI;

namespace Aife.Api.Tests;

internal sealed class StageAwareFakeProvider : ILlmProvider
{
    private readonly Dictionary<string, string> _responsesByStage;

    public StageAwareFakeProvider(Dictionary<string, string> responsesByStage)
    {
        _responsesByStage = responsesByStage;
        Info = new LlmProviderInfo
        {
            Name = "fake",
            Priority = 1,
            Capabilities = new List<string> { "text", "json" }
        };
    }

    public LlmProviderInfo Info { get; }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var stageName = request.StageContext?.StageName ?? "default";
        var text = _responsesByStage.GetValueOrDefault(stageName, "{}");

        return Task.FromResult(new LlmResponse
        {
            Text = text,
            FinishReason = "stop",
            ProviderName = Info.Name
        });
    }
}
