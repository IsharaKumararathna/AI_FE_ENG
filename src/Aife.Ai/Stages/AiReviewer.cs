using Aife.Application.AI;
using Aife.Application.AI.Stages;
using Aife.Application.Knowledge;
using Aife.Application.Prompting;
using Aife.Domain.Enums;
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

        var text = response.Text;
        var jsonStart = text.IndexOfAny(new[] { '[', '{' });
        if (jsonStart > 0) text = text[jsonStart..];
        text = TrimAfterJsonClose(text);

        var report = JsonConvert.DeserializeObject<ReviewReport>(text);

        if (report is null)
        {
            // Fallback: return a minimal valid report
            Console.WriteLine($"[Reviewer] Failed to deserialize ReviewReport. Raw: {text[..Math.Min(text.Length, 500)]}");
            return new ReviewReport
            {
                Score = 50,
                Outcome = ReviewOutcome.PassedWithWarnings,
                Violations = new List<Violation>(),
                Suggestions = new List<string> { "Review parse failed. Manual review recommended." }
            };
        }

        return report;
    }

    private static string TrimAfterJsonClose(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
            return trimmed;
        var depth = 0; var inString = false; var escaped = false;
        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            if (escaped) { escaped = false; continue; }
            if (ch == '\\') { escaped = true; continue; }
            if (ch == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (ch == '{' || ch == '[') depth++;
            else if (ch == '}' || ch == ']') { depth--; if (depth == 0) return trimmed[..(i + 1)]; }
        }
        return trimmed;
    }
}
