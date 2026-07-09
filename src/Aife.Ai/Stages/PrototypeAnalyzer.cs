using Aife.Application.AI;
using Aife.Application.AI.Stages;
using Aife.Application.Prompting;
using Aife.Domain.Generation;
using Newtonsoft.Json;

namespace Aife.Ai.Stages;

/// <summary>
/// Detects layout regions and UI primitives in a prototype by calling the LLM
/// Router with the <c>prototype.analyzer</c> prompt.
/// </summary>
public sealed class PrototypeAnalyzer : IPrototypeAnalyzer
{
    private readonly LlmRouter _router;
    private readonly IPromptManager _promptManager;

    public PrototypeAnalyzer(LlmRouter router, IPromptManager promptManager)
    {
        _router = router;
        _promptManager = promptManager;
    }

    public async Task<PrototypeAnalysis> AnalyzeAsync(Prototype prototype, CancellationToken ct)
    {
        var prompt = await _promptManager.GetPromptAsync(
            "prototype.analyzer",
            new Dictionary<string, string>
            {
                { "html", prototype.Html },
                { "css", prototype.Css }
            },
            ct);

        var request = new LlmRequest
        {
            SystemMessage = prompt.SystemMessage,
            UserMessage = $"HTML:\n{prototype.Html}\n\nCSS:\n{prototype.Css}",
            JsonMode = true,
            StageContext = new StageContext { StageName = "Analyze" }
        };

        var response = await _router.CompleteAsync(request, ct);

        var analysis = JsonConvert.DeserializeObject<PrototypeAnalysis>(response.Text)
            ?? throw new InvalidOperationException("Failed to deserialize PrototypeAnalysis from LLM response.");

        return analysis;
    }
}
