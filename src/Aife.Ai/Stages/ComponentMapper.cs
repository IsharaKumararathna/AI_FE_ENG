using Aife.Application.AI;
using Aife.Application.AI.Stages;
using Aife.Application.Knowledge;
using Aife.Application.Prompting;
using Aife.Domain.Generation;
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
        var candidates = new List<ComponentSummary>();

        foreach (var summary in await _knowledgeProvider.SearchComponentsAsync(new ComponentQuery(), ct))
        {
            var detail = await _knowledgeProvider.GetComponentAsync(summary.ComponentId, ct);

            // Exact mapsFromHtml match
            if (detail?.MapsFromHtml is not null &&
                detail.MapsFromHtml.Any(h => h.Equals(elementKind, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(summary);
                continue;
            }

            // Semantic fallback: match by element kind → component category
            if (MatchesSemantically(elementKind, summary.Category, detail))
            {
                candidates.Add(summary);
            }
        }

        return candidates;
    }

    private static bool MatchesSemantically(string elementKind, string category, ComponentDetail? detail)
    {
        // Semantic fallback table: analyzer's "kind" → KB component category
        var fallback = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Buttons
            ["button"] = "button",
            // Tables & grids
            ["table"] = "table",
            ["datagrid"] = "table",
            ["grid"] = "table",
            // Inputs
            ["input"] = "input",
            ["search"] = "input",
            ["search input"] = "input",
            ["textbox"] = "input",
            ["textarea"] = "input",
            ["select"] = "input",
            ["dropdown"] = "input",
            // Navigation / tabs
            ["tabs"] = "navigation",
            ["tab"] = "navigation",
            ["tabstrip"] = "navigation",
            ["sidebar"] = "navigation",
            ["navigation"] = "navigation",
            ["nav"] = "navigation",
            ["menu"] = "navigation",
            // Forms
            ["form"] = "form",
            ["formfield"] = "form",
            ["checkbox"] = "form",
            ["switch"] = "form",
            ["toggle"] = "form",
            ["radio"] = "form",
            // Layout / structure
            ["header"] = "layout",
            ["footer"] = "layout",
            ["card"] = "layout",
            ["dialog"] = "layout",
            ["modal"] = "layout",
            ["layout"] = "layout",
            // Chips / badges
            ["chips"] = "display",
            ["chip"] = "display",
            ["badge"] = "display",
            ["tag"] = "display",
            ["label"] = "display",
            // Typography
            ["typography"] = "display",
            ["text"] = "display",
            ["heading"] = "display",
            ["title"] = "display",
            // Misc
            ["icon"] = "display",
            ["image"] = "display",
            ["avatar"] = "display"
        };

        if (!fallback.TryGetValue(elementKind, out var expectedCategory))
            return false;

        return string.Equals(category, expectedCategory, StringComparison.OrdinalIgnoreCase);
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
