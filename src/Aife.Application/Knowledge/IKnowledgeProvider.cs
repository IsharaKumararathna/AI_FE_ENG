namespace Aife.Application.Knowledge;

/// <summary>
/// The only abstraction through which AI stages and the mapper access Design
/// System knowledge. The MVP ships <c>JsonKnowledgeProvider</c>; Phase 2 ships
/// <c>McpKnowledgeProvider</c> against the same contract (ADR-003).
///
/// The method set maps one-to-one onto the Phase 2 MCP tools so the MCP
/// provider is a direct swap. <see cref="GetReferenceUiPatternsAsync" /> was
/// added by ADR-005 for input-side conformance review.
/// </summary>
public interface IKnowledgeProvider
{
    Task<IReadOnlyList<ComponentSummary>> SearchComponentsAsync(ComponentQuery query, CancellationToken ct);

    Task<ComponentDetail?> GetComponentAsync(string componentId, CancellationToken ct);

    Task<ComponentProps?> GetComponentPropsAsync(string componentId, CancellationToken ct);

    Task<IReadOnlyList<ComponentExample>> GetComponentExamplesAsync(string componentId, CancellationToken ct);

    Task<IReadOnlyList<LayoutPattern>> GetLayoutPatternsAsync(CancellationToken ct);

    Task<DesignTokenSet> GetDesignTokensAsync(CancellationToken ct);

    Task<IReadOnlyList<Icon>> GetIconsAsync(IconQuery query, CancellationToken ct);

    Task<IReadOnlyList<ComponentSummary>> SearchComponentsByDescriptionAsync(string description, CancellationToken ct);

    Task<IReadOnlyList<BestPractice>> GetBestPracticesAsync(BestPracticeQuery query, CancellationToken ct);

    Task<IReadOnlyList<AccessibilityRule>> GetAccessibilityRulesAsync(CancellationToken ct);

    /// <summary>
    /// Returns reference UI patterns drawn from current production UI (ADR-005).
    /// Used by the conformance reviewer and the prototype generator.
    /// </summary>
    Task<IReadOnlyList<ReferenceUiPattern>> GetReferenceUiPatternsAsync(CancellationToken ct);
}
