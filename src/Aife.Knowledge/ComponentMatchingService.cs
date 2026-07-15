using Aife.Application.Knowledge;

namespace Aife.Knowledge;

/// <summary>
/// A single ranked candidate returned for an HTML element kind. Carries
/// everything a host agent needs to write a correct import (ADR: never
/// invent component names/paths — always use what the KB reports).
/// </summary>
public sealed record ComponentMatch
{
    public required string ComponentId { get; init; }
    public required string Name { get; init; }
    public string? ImportPath { get; init; }
    public string? ExportName { get; init; }
    public bool IsDefaultExport { get; init; }
    public required double Confidence { get; init; }
    public required string Reason { get; init; }
}

/// <summary>
/// Deterministic (no LLM) matching of HTML element kinds to approved Design
/// System components. This is the single source of truth for the
/// HTML-kind → category fallback table, previously duplicated between
/// <c>ComponentMapper</c> and <c>PrototypeConformanceReviewer</c> in
/// Aife.Ai, and now also backing the MCP <c>match_element</c> tool so both
/// the legacy HTTP pipeline and any host coding agent get identical results.
/// </summary>
public sealed class ComponentMatchingService
{
    private readonly IKnowledgeProvider _knowledgeProvider;

    public ComponentMatchingService(IKnowledgeProvider knowledgeProvider)
    {
        _knowledgeProvider = knowledgeProvider;
    }

    /// <summary>
    /// Returns ranked candidate components for a detected HTML element kind
    /// (e.g. "button", "table"). Exact <c>mapsFromHtml</c> matches (confidence
    /// 1.0, optionally boosted by text similarity) are preferred; if none
    /// exist, falls back to category-based semantic matches (confidence 0.6).
    /// Mixing the two is deliberately avoided — a real KB can contain many
    /// components sharing a category (e.g. a grid's internal sub-pieces)
    /// without all of them declaring an explicit HTML mapping, so treating an
    /// exact match as authoritative avoids forcing ambiguous disambiguation
    /// between a real component and its internals.
    /// </summary>
    public async Task<IReadOnlyList<ComponentMatch>> MatchElementAsync(
        string kind,
        string? text,
        CancellationToken ct)
    {
        var exact = new List<ComponentMatch>();
        var semantic = new List<ComponentMatch>();

        foreach (var summary in await _knowledgeProvider.SearchComponentsAsync(new ComponentQuery(), ct))
        {
            var detail = await _knowledgeProvider.GetComponentAsync(summary.ComponentId, ct);
            if (detail is null)
                continue;

            if (detail.MapsFromHtml is not null &&
                detail.MapsFromHtml.Any(h => h.Equals(kind, StringComparison.OrdinalIgnoreCase)))
            {
                exact.Add(BuildMatch(detail, 1.0, "Exact match via mapsFromHtml", text));
                continue;
            }

            if (MatchesSemantically(kind, summary.Category))
            {
                semantic.Add(BuildMatch(detail, 0.6, $"Category match ('{summary.Category}')", text));
            }
        }

        var results = exact.Count > 0 ? exact : semantic;
        return results.OrderByDescending(m => m.Confidence).ToList();
    }

    private static ComponentMatch BuildMatch(ComponentDetail detail, double baseConfidence, string reason, string? text)
    {
        var confidence = baseConfidence;

        var boost = TextSimilarityBoost(text, detail.Name, detail.Description);
        if (boost > 0)
        {
            confidence = Math.Min(1.0, confidence + boost);
            reason += $"; text similarity boost (+{boost:0.00})";
        }

        return new ComponentMatch
        {
            ComponentId = detail.ComponentId,
            Name = detail.Name,
            ImportPath = detail.ImportPath,
            ExportName = detail.ExportName,
            IsDefaultExport = detail.IsDefaultExport,
            Confidence = confidence,
            Reason = reason
        };
    }

    private static double TextSimilarityBoost(string? text, string componentName, string? description)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0.0;

        var lowerText = text.ToLowerInvariant();
        var lowerName = componentName.ToLowerInvariant().Replace(" ", "");

        if (lowerText.Replace(" ", "").Contains(lowerName) || lowerName.Contains(lowerText.Replace(" ", "")))
            return 0.1;

        if (description is not null && description.ToLowerInvariant().Contains(lowerText))
            return 0.05;

        return 0.0;
    }

    /// <summary>
    /// Canonical HTML-kind → component-category fallback table. Used whenever
    /// no component declares an exact <c>mapsFromHtml</c> entry for the kind.
    /// Delegates to <see cref="ElementCategoryFallback"/> (Aife.Application) so
    /// the legacy HTTP pipeline (<c>ComponentMapper</c>,
    /// <c>PrototypeConformanceReviewer</c>, <c>UiTreeAssembler</c>) and this MCP
    /// matching service share one single-sourced table.
    /// </summary>
    public static bool MatchesSemantically(string elementKind, string category) =>
        ElementCategoryFallback.MatchesSemantically(elementKind, category);
}

