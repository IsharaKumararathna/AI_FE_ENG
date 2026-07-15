using Aife.Application.AI;
using Aife.Application.AI.Stages;
using Aife.Application.Knowledge;
using Aife.Application.Prompting;
using Aife.Domain.Generation;
using Aife.Knowledge;
using Newtonsoft.Json;

namespace Aife.Ai.Stages;

/// <summary>
/// Maps detected elements to approved Design System components. Uses a
/// deterministic baseline (mapsFromHtml) for unambiguous matches and LLM
/// refinement for ambiguous candidates. Unmatched elements get null componentId
/// and confidence 0.
/// </summary>
public sealed class ComponentMapper : IComponentMapper
{
    private readonly LlmRouter _router;
    private readonly IPromptManager _promptManager;
    private readonly IKnowledgeProvider _knowledgeProvider;

    public ComponentMapper(LlmRouter router, IPromptManager promptManager, IKnowledgeProvider knowledgeProvider)
    {
        _router = router;
        _promptManager = promptManager;
        _knowledgeProvider = knowledgeProvider;
    }

    public async Task<IReadOnlyList<ComponentMapping>> MapAsync(PrototypeAnalysis analysis, CancellationToken ct)
    {
        var allComponents = await _knowledgeProvider.SearchComponentsAsync(new ComponentQuery(), ct);
        var mappings = new List<ComponentMapping>();

        foreach (var element in analysis.Elements)
        {
            var candidates = await FindCandidates(element.Kind, ct);

            if (candidates.Count == 1)
            {
                mappings.Add(new ComponentMapping
                {
                    ElementRef = element.Kind,
                    ComponentId = candidates[0].ComponentId,
                    Confidence = 0.95,
                    Reason = "Baseline match via mapsFromHtml"
                });
            }
            else if (candidates.Count > 1)
            {
                var mapping = await RefineWithLlm(element, candidates, ct);
                mappings.Add(mapping);
            }
            else
            {
                mappings.Add(new ComponentMapping
                {
                    ElementRef = element.Kind,
                    ComponentId = null,
                    Confidence = 0.0,
                    Reason = "No approved component matches this element kind."
                });
            }
        }

        return mappings;
    }

    private async Task<List<ComponentSummary>> FindCandidates(string elementKind, CancellationToken ct)
    {
        var exactMatches = new List<ComponentSummary>();
        var semanticMatches = new List<ComponentSummary>();

        foreach (var summary in await _knowledgeProvider.SearchComponentsAsync(new ComponentQuery(), ct))
        {
            var detail = await _knowledgeProvider.GetComponentAsync(summary.ComponentId, ct);

            // Exact mapsFromHtml match
            if (detail?.MapsFromHtml is not null &&
                detail.MapsFromHtml.Any(h => h.Equals(elementKind, StringComparison.OrdinalIgnoreCase)))
            {
                exactMatches.Add(summary);
                continue;
            }

            // Semantic fallback: match by element kind → component category
            if (ComponentMatchingService.MatchesSemantically(elementKind, summary.Category))
            {
                semanticMatches.Add(summary);
            }
        }

        // Prefer exact mapsFromHtml matches over the weaker category-based
        // semantic fallback: a real KB can contain multiple components that
        // share a category (e.g. a grid's internal sub-pieces also
        // classified as "table") without all of them declaring an explicit
        // HTML mapping. If any exact match exists, treat it as authoritative
        // and ignore semantic-only matches rather than forcing ambiguous
        // LLM disambiguation between a real component and its internals.
        return exactMatches.Count > 0 ? exactMatches : semanticMatches;
    }

    private async Task<ComponentMapping> RefineWithLlm(
        DetectedElement element,
        List<ComponentSummary> candidates,
        CancellationToken ct)
    {
        var prompt = await _promptManager.GetPromptAsync(
            "component.mapper",
            new Dictionary<string, string>
            {
                { "detectedElements", JsonConvert.SerializeObject(element) },
                { "availableComponents", JsonConvert.SerializeObject(candidates) }
            },
            ct);

        var request = new LlmRequest
        {
            SystemMessage = prompt.SystemMessage,
            UserMessage = JsonConvert.SerializeObject(element),
            JsonMode = true,
            StageContext = new StageContext { StageName = "Map" }
        };

        var response = await _router.CompleteAsync(request, ct);

        // Trim extra text after JSON close (same as PrototypeAnalyzer)
        var text = TrimAfterJsonClose(response.Text);

        ComponentMapping? mapping = null;
        try
        {
            mapping = JsonConvert.DeserializeObject<ComponentMapping>(text);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Mapper] LLM refine parse error: {ex.Message}. Raw: {response.Text[..Math.Min(response.Text.Length, 300)]}");
        }

        return mapping ?? new ComponentMapping
        {
            ElementRef = element.Kind,
            ComponentId = candidates[0].ComponentId,
            Confidence = 0.80,
            Reason = "LLM refinement; fell back to first candidate."
        };
    }

    private static string TrimAfterJsonClose(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
            return trimmed;

        var depth = 0;
        var inString = false;
        var escaped = false;

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
