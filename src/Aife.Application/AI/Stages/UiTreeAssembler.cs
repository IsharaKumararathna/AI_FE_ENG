using Aife.Application.Knowledge;
using Aife.Domain.Generation;

namespace Aife.Application.AI.Stages;

/// <summary>
/// Default <see cref="IUiTreeAssembler" />. Places auto-applied mappings (confidence
/// ≥ 0.5) into the tree and resolves token bindings from the Knowledge Base.
/// </summary>
public sealed class UiTreeAssembler : IUiTreeAssembler
{
    private readonly IKnowledgeProvider _knowledgeProvider;

    public UiTreeAssembler(IKnowledgeProvider knowledgeProvider)
    {
        _knowledgeProvider = knowledgeProvider;
    }

    public async Task<IntermediateUiTree> AssembleAsync(
        PrototypeAnalysis analysis,
        IReadOnlyList<ComponentMapping> mappings,
        CancellationToken ct)
    {
        var tree = new IntermediateUiTree
        {
            Page = "Generated",
            Layout = analysis.Layout,
            Children = new List<UiNode>()
        };

        var nodeIndex = 0;
        var seenComponents = new HashSet<string>(); // deduplicate by component+kind

        // First pass: add confident mappings
        foreach (var mapping in mappings.Where(m => m.Confidence >= 0.5 && m.ComponentId is not null))
        {
            var key = $"confident:{mapping.ComponentId}";
            if (seenComponents.Contains(key))
                continue;
            seenComponents.Add(key);

            nodeIndex++;
            var component = await _knowledgeProvider.GetComponentAsync(mapping.ComponentId!, ct);
            var tokenBindings = new Dictionary<string, string>();

            if (component?.TokensConsumed is not null)
            {
                foreach (var token in component.TokensConsumed)
                {
                    tokenBindings[token] = token;
                }
            }

            tree.Children.Add(new UiNode
            {
                NodeId = $"n-{nodeIndex}",
                ComponentId = mapping.ComponentId!,
                TokenBindings = tokenBindings
            });
        }

        // Second pass: add best-guess fallbacks for unmapped elements.
        // Limit to one per component type (not one per element) to keep
        // the tree focused and avoid overwhelming the generator.
        var fallbackSeen = new HashSet<string>();
        foreach (var mapping in mappings.Where(m => m.Confidence < 0.5 || m.ComponentId is null))
        {
            var fallbackId = await GetFallbackComponentIdAsync(mapping.ElementRef ?? "", ct);
            if (fallbackSeen.Contains(fallbackId))
                continue;
            fallbackSeen.Add(fallbackId);

            // Skip elements that are purely structural (sidebar, header, etc.)
            // — the generator should produce the full layout structure itself.
            if (mapping.ElementRef is "sidebar" or "header" or "footer" or "typography" or "avatar" or "")
                continue;

            nodeIndex++;
            tree.Children.Add(new UiNode
            {
                NodeId = $"n-{nodeIndex}",
                ComponentId = fallbackId,
                Props = new Dictionary<string, object?>
                {
                    ["label"] = mapping.ElementRef ?? "element"
                }
            });

            Console.WriteLine($"[Assembler] Unmapped element '{mapping.ElementRef}' → fallback '{fallbackId}' (confidence={mapping.Confidence:F2})");
        }

        if (tree.Children.Count == 0)
        {
            // Never leave an empty tree — add minimal structure
            tree.Children.Add(new UiNode
            {
                NodeId = "n-1",
                ComponentId = "BUSButton",
                Props = new Dictionary<string, object?> { ["label"] = "Generated" }
            });
        }

        Console.WriteLine($"[Assembler] Tree assembled: layout={tree.Layout}, {tree.Children.Count} unique nodes");

        return tree;
    }

    /// <summary>
    /// Resolves a best-guess component for an element the primary mapper
    /// couldn't confidently place, by querying the KB directly rather than
    /// hardcoding project-specific component IDs (a KB trained from a
    /// different Core repo won't have e.g. "BUSButton"). Prefers an exact
    /// <c>mapsFromHtml</c> declaration, then a category match via the shared
    /// <see cref="ElementCategoryFallback"/> table, then any approved
    /// component at all so the tree still renders something real.
    /// </summary>
    private async Task<string> GetFallbackComponentIdAsync(string elementRef, CancellationToken ct)
    {
        var kind = (elementRef ?? string.Empty).ToLowerInvariant();
        var allComponents = await _knowledgeProvider.SearchComponentsAsync(new ComponentQuery(), ct);

        foreach (var summary in allComponents)
        {
            var detail = await _knowledgeProvider.GetComponentAsync(summary.ComponentId, ct);
            if (detail?.MapsFromHtml is not null &&
                detail.MapsFromHtml.Any(h => h.Equals(kind, StringComparison.OrdinalIgnoreCase)))
            {
                return summary.ComponentId;
            }
        }

        var categoryMatch = allComponents.FirstOrDefault(s => ElementCategoryFallback.MatchesSemantically(kind, s.Category));
        if (categoryMatch is not null)
            return categoryMatch.ComponentId;

        return allComponents.FirstOrDefault()?.ComponentId ?? "BUSButton";
    }
}
