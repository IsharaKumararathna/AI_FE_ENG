using Aife.Application.AI;
using Aife.Application.AI.Stages;
using Aife.Application.Knowledge;
using Aife.Application.Prompting;
using Aife.Domain.Generation;
using Newtonsoft.Json;

namespace Aife.Ai.Stages;

/// <summary>
/// Scores generated output for compliance, accessibility, and architecture.
/// Gathers rules and approved components from the Knowledge Base before
/// scoring via the LLM Router.
/// </summary>
public sealed class AiReviewer : IAiReviewer
{
    private readonly LlmRouter _router;
    private readonly IPromptManager _promptManager;
    private readonly IKnowledgeProvider _knowledgeProvider;

    public AiReviewer(LlmRouter router, IPromptManager promptManager, IKnowledgeProvider knowledgeProvider)
    {
        _router = router;
        _promptManager = promptManager;
        _knowledgeProvider = knowledgeProvider;
    }

    public async Task<ReviewReport> ReviewAsync(
        IReadOnlyList<GeneratedArtifact> artifacts,
        IntermediateUiTree tree,
        CancellationToken ct)
    {
        var approvedComponents = await _knowledgeProvider.SearchComponentsAsync(new ComponentQuery(), ct);
        var tokens = await _knowledgeProvider.GetDesignTokensAsync(ct);
        var accessibilityRules = await _knowledgeProvider.GetAccessibilityRulesAsync(ct);

        var prompt = await _promptManager.GetPromptAsync(
            "ai.reviewer",
            new Dictionary<string, string>
            {
                { "artifacts", JsonConvert.SerializeObject(artifacts) },
                { "intermediateUiTree", JsonConvert.SerializeObject(tree) },
                { "approvedComponents", JsonConvert.SerializeObject(approvedComponents) },
                { "tokens", JsonConvert.SerializeObject(tokens) },
                { "accessibilityRules", JsonConvert.SerializeObject(accessibilityRules) }
            },
            ct);

        var request = new LlmRequest
        {
            SystemMessage = prompt.SystemMessage,
            UserMessage = JsonConvert.SerializeObject(new { artifacts, tree }),
            JsonMode = true,
            StageContext = new StageContext { StageName = "Review" }
        };

        var response = await _router.CompleteAsync(request, ct);

        var report = JsonConvert.DeserializeObject<ReviewReport>(response.Text)
            ?? throw new InvalidOperationException("Failed to deserialize ReviewReport from LLM response.");

        return report;
    }
}
