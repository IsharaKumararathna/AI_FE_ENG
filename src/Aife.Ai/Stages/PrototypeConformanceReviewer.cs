using System.Text.RegularExpressions;
using Aife.Application.AI;
using Aife.Application.AI.Stages;
using Aife.Application.Knowledge;
using Aife.Application.Prompting;
using Aife.Domain.Enums;
using Aife.Domain.Generation;
using Aife.Knowledge;
using Newtonsoft.Json;

namespace Aife.Ai.Stages;

/// <summary>
/// Reviews an uploaded prototype for Design System drift (ADR-005). Combines
/// deterministic checks (hardcoded colors, unmapped elements) with an LLM step
/// for deeper analysis. Advisory and non-blocking in the MVP.
/// </summary>
public sealed class PrototypeConformanceReviewer : IPrototypeConformanceReviewer
{
    private static readonly Regex HexColorRegex =
        new(@"#(?:[0-9a-fA-F]{3}){1,2}\b", RegexOptions.Compiled);

    private static readonly Regex RgbColorRegex =
        new(@"rgba?\(\s*\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly LlmRouter _router;
    private readonly IPromptManager _promptManager;
    private readonly IKnowledgeProvider _knowledgeProvider;

    public PrototypeConformanceReviewer(
        LlmRouter router,
        IPromptManager promptManager,
        IKnowledgeProvider knowledgeProvider)
    {
        _router = router;
        _promptManager = promptManager;
        _knowledgeProvider = knowledgeProvider;
    }

    public async Task<PrototypeConformanceReport> ReviewAsync(
        Prototype prototype,
        PrototypeAnalysis analysis,
        CancellationToken ct)
    {
        var findings = new List<ConformanceFinding>();
        var suggestions = new List<string>();

        // ── Deterministic checks ──
        await CheckHardcodedColorsAsync(prototype, findings, suggestions, ct);
        await CheckUnmappedElementsAsync(analysis, findings, suggestions, ct);
        await CheckLayoutConformanceAsync(analysis, findings, suggestions, ct);

        // ── LLM step for deeper analysis ──
        var llmReport = await ReviewWithLlm(prototype, analysis, ct);
        if (llmReport.Findings is not null)
            findings.AddRange(llmReport.Findings);
        if (llmReport.Suggestions is not null)
            suggestions.AddRange(llmReport.Suggestions);

        var outcome = findings.Any(f => f.Severity == Severity.Blocking)
            ? ReviewOutcome.Failed
            : findings.Count > 0
                ? ReviewOutcome.PassedWithWarnings
                : ReviewOutcome.Passed;

        return new PrototypeConformanceReport
        {
            PrototypeId = prototype.Id,
            Outcome = outcome,
            Findings = findings,
            Suggestions = suggestions
        };
    }

    private async Task CheckHardcodedColorsAsync(
        Prototype prototype,
        List<ConformanceFinding> findings,
        List<string> suggestions,
        CancellationToken ct)
    {
        var tokens = await _knowledgeProvider.GetDesignTokensAsync(ct);
        var tokenColors = tokens.Tokens
            .Where(t => t.Category.Equals("color", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var combined = $"{prototype.Html}\n{prototype.Css}";

        var hexMatches = HexColorRegex.Matches(combined);
        foreach (Match match in hexMatches)
        {
            var color = match.Value;
            if (!tokenColors.Contains(color))
            {
                var nearest = FindNearestToken(color, tokenColors);
                findings.Add(new ConformanceFinding
                {
                    RuleId = "CONF_COLOR_OFF_TOKEN",
                    Category = "Token conformance",
                    Severity = Severity.Advisory,
                    Message = $"Hardcoded color '{color}' is not in the design token palette.",
                    Location = $"Position {match.Index}"
                });

                if (nearest is not null)
                    suggestions.Add($"Replace '{color}' with the nearest token '{nearest}'.");
            }
        }

        var rgbMatches = RgbColorRegex.Matches(combined);
        foreach (Match match in rgbMatches)
        {
            findings.Add(new ConformanceFinding
            {
                RuleId = "CONF_RGB_COLOR",
                Category = "Token conformance",
                Severity = Severity.Advisory,
                Message = "RGB/RGBA color literal found. Use design tokens only.",
                Location = $"Position {match.Index}"
            });
        }
    }

    private async Task CheckUnmappedElementsAsync(
        PrototypeAnalysis analysis,
        List<ConformanceFinding> findings,
        List<string> suggestions,
        CancellationToken ct)
    {
        var allComponents = await _knowledgeProvider.SearchComponentsAsync(new ComponentQuery(), ct);

        foreach (var element in analysis.Elements)
        {
            if (string.IsNullOrWhiteSpace(element.Kind))
                continue;

            var hasMapping = false;
            foreach (var summary in allComponents)
            {
                var detail = await _knowledgeProvider.GetComponentAsync(summary.ComponentId, ct);

                // Check mapsFromHtml exact match
                if (detail?.MapsFromHtml is not null &&
                    detail.MapsFromHtml.Any(h => h.Equals(element.Kind, StringComparison.OrdinalIgnoreCase)))
                {
                    hasMapping = true;
                    break;
                }

                // Check semantic fallback match
                if (ComponentMatchingService.MatchesSemantically(element.Kind, summary.Category))
                {
                    hasMapping = true;
                    break;
                }
            }

            if (!hasMapping)
            {
                findings.Add(new ConformanceFinding
                {
                    RuleId = "CONF_UNMAPPED_ELEMENT",
                    Category = "Component conformance",
                    Severity = Severity.Advisory,
                    Message = $"Element kind '{element.Kind}' has no approved Design System component mapping.",
                    Location = $"Element: {element.Kind}"
                });
            }
        }
    }

    private async Task CheckLayoutConformanceAsync(
        PrototypeAnalysis analysis,
        List<ConformanceFinding> findings,
        List<string> suggestions,
        CancellationToken ct)
    {
        var layouts = await _knowledgeProvider.GetLayoutPatternsAsync(ct);
        var layoutIds = layouts.Select(l => l.LayoutId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!layoutIds.Contains(analysis.Layout))
        {
            findings.Add(new ConformanceFinding
            {
                RuleId = "CONF_LAYOUT_NOT_APPROVED",
                Category = "Layout conformance",
                Severity = Severity.Advisory,
                Message = $"Layout '{analysis.Layout}' is not an approved layout pattern.",
                Location = "Layout"
            });
        }

        // Check against reference UI patterns
        var referencePatterns = await _knowledgeProvider.GetReferenceUiPatternsAsync(ct);
        foreach (var pattern in referencePatterns)
        {
            if (pattern.LayoutId.Equals(analysis.Layout, StringComparison.OrdinalIgnoreCase))
            {
                suggestions.Add($"Reference UI pattern '{pattern.PatternId}' is available for layout '{analysis.Layout}'. Consider mirroring its structure.");
            }
        }
    }

    private async Task<PrototypeConformanceReport> ReviewWithLlm(
        Prototype prototype,
        PrototypeAnalysis analysis,
        CancellationToken ct)
    {
        var tokens = await _knowledgeProvider.GetDesignTokensAsync(ct);
        var layouts = await _knowledgeProvider.GetLayoutPatternsAsync(ct);
        var referencePatterns = await _knowledgeProvider.GetReferenceUiPatternsAsync(ct);
        var accessibilityRules = await _knowledgeProvider.GetAccessibilityRulesAsync(ct);

        var prompt = await _promptManager.GetPromptAsync(
            "prototype.conformance.reviewer",
            new Dictionary<string, string>
            {
                { "prototypeAnalysis", JsonConvert.SerializeObject(analysis) },
                { "html", prototype.Html },
                { "css", prototype.Css },
                { "tokens", JsonConvert.SerializeObject(tokens) },
                { "layoutPatterns", JsonConvert.SerializeObject(layouts) },
                { "referenceUiPatterns", JsonConvert.SerializeObject(referencePatterns) },
                { "accessibilityRules", JsonConvert.SerializeObject(accessibilityRules) }
            },
            ct);

        var request = new LlmRequest
        {
            SystemMessage = prompt.SystemMessage,
            UserMessage = JsonConvert.SerializeObject(new { analysis, html = prototype.Html, css = prototype.Css }),
            JsonMode = true,
            StageContext = new StageContext { StageName = "ConformanceReview" }
        };

        var response = await _router.CompleteAsync(request, ct);

        // Extract JSON from LLM response
        var text = response.Text;
        var jsonStart = text.IndexOfAny(new[] { '[', '{' });
        if (jsonStart > 0)
            text = text[jsonStart..];

        PrototypeConformanceReport? report = null;
        try
        {
            report = JsonConvert.DeserializeObject<PrototypeConformanceReport>(text);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConformanceReviewer] Failed to parse LLM response: {ex.Message}. Raw: {text[..Math.Min(text.Length, 200)]}");
        }

        return report ?? new PrototypeConformanceReport
        {
            PrototypeId = prototype.Id,
            Outcome = ReviewOutcome.Passed,
            Findings = new List<ConformanceFinding>(),
            Suggestions = new List<string>()
        };
    }

    private static string? FindNearestToken(string color, HashSet<string> tokenColors)
    {
        // Simple MVP: return the first token that contains the color value.
        // A real implementation would compute color distance.
        return tokenColors.FirstOrDefault(c => c.Equals(color, StringComparison.OrdinalIgnoreCase));
    }
}
