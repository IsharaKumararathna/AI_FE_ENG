using Aife.Application.Knowledge;

namespace Aife.Knowledge;

/// <summary>Input describing one detected/mapped element for scoring.</summary>
public sealed record ScoredElementInput
{
    public required string Kind { get; init; }
    public string? Text { get; init; }
    public string? MatchedComponentId { get; init; }
    public double Confidence { get; init; }
}

public sealed record PrototypeScoreResult
{
    public required double FinalScore { get; init; }
    public required double MatchScore { get; init; }
    public required double TokenScore { get; init; }
    public required double A11yScore { get; init; }
    public required IReadOnlyList<string> UnmatchedElements { get; init; }
    public required IReadOnlyList<string> Suggestions { get; init; }
}

/// <summary>
/// Combines element-match coverage, token conformance, and accessibility
/// coverage into one 0-100 conformance score using equal weighting (user
/// decision), so non-technical users get a single actionable number plus
/// detail on what didn't match instead of having to interpret raw findings.
/// </summary>
public sealed class PrototypeScorer
{
    private const double MatchConfidenceThreshold = 0.5;

    private readonly IKnowledgeProvider _knowledgeProvider;

    public PrototypeScorer(IKnowledgeProvider knowledgeProvider)
    {
        _knowledgeProvider = knowledgeProvider;
    }

    public async Task<PrototypeScoreResult> ScoreAsync(
        IReadOnlyList<ScoredElementInput> elements,
        IReadOnlyList<TokenViolation> tokenViolations,
        CancellationToken ct)
    {
        var suggestions = new List<string>();
        var unmatched = new List<string>();

        // ── Match score: % of elements matched with acceptable confidence ──
        double matchScore;
        if (elements.Count == 0)
        {
            matchScore = 100;
        }
        else
        {
            var matchedCount = 0;
            foreach (var element in elements)
            {
                var isMatched = !string.IsNullOrWhiteSpace(element.MatchedComponentId)
                    && element.Confidence >= MatchConfidenceThreshold;

                if (isMatched)
                {
                    matchedCount++;
                }
                else
                {
                    unmatched.Add(element.Kind);
                    suggestions.Add(
                        $"No confident component match for '{element.Kind}'" +
                        (string.IsNullOrWhiteSpace(element.Text) ? "." : $" ('{element.Text}')") +
                        " — call match_element again with more context, or confirm no approved component covers it.");
                }
            }

            matchScore = 100.0 * matchedCount / elements.Count;
        }

        // ── Token score: pre-computed by check_token_conformance ──
        var tokenScore = tokenViolations.Count == 0 ? 100.0 : Math.Max(0, 100 - (tokenViolations.Count * 10));
        foreach (var violation in tokenViolations)
            suggestions.Add(violation.Message + (violation.SuggestedToken is null ? "" : $" Consider token '{violation.SuggestedToken}'."));

        // ── Accessibility score: % of matched components with declared a11y metadata ──
        var matchedComponentIds = elements
            .Where(e => !string.IsNullOrWhiteSpace(e.MatchedComponentId) && e.Confidence >= MatchConfidenceThreshold)
            .Select(e => e.MatchedComponentId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        double a11yScore;
        if (matchedComponentIds.Count == 0)
        {
            a11yScore = 100;
        }
        else
        {
            var withA11y = 0;
            foreach (var componentId in matchedComponentIds)
            {
                var detail = await _knowledgeProvider.GetComponentAsync(componentId, ct);
                var hasA11y = detail?.Accessibility is not null &&
                    (!string.IsNullOrWhiteSpace(detail.Accessibility.Role)
                        || detail.Accessibility.KeyboardSupport is true
                        || (detail.Accessibility.AriaProps?.Count ?? 0) > 0);

                if (hasA11y)
                {
                    withA11y++;
                }
                else
                {
                    suggestions.Add($"Component '{componentId}' has no accessibility metadata in the Knowledge Base — verify ARIA roles/keyboard support manually.");
                }
            }

            a11yScore = 100.0 * withA11y / matchedComponentIds.Count;
        }

        var finalScore = (matchScore + tokenScore + a11yScore) / 3.0;

        return new PrototypeScoreResult
        {
            FinalScore = Math.Round(finalScore, 1),
            MatchScore = Math.Round(matchScore, 1),
            TokenScore = Math.Round(tokenScore, 1),
            A11yScore = Math.Round(a11yScore, 1),
            UnmatchedElements = unmatched,
            Suggestions = suggestions
        };
    }
}
