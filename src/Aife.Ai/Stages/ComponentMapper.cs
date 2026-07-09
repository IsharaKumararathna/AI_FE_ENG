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
            if (detail?.MapsFromHtml is not null &&
                detail.MapsFromHtml.Any(h => h.Equals(elementKind, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(summary);
            }
        }

        return candidates;
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

        var mapping = JsonConvert.DeserializeObject<ComponentMapping>(response.Text);

        return mapping ?? new ComponentMapping
        {
            ElementRef = element.Kind,
            ComponentId = candidates[0].ComponentId,
            Confidence = 0.80,
            Reason = "LLM refinement; fell back to first candidate."
        };
    }
}
