namespace Aife.Application.Knowledge;

/// <summary>
/// An approved page/layout pattern drawn from current production UI, used by the
/// Prototype Conformance Review (ADR-005) and the Prototype Generator (ADR-006)
/// to detect drift and stay "similar to current UI."
/// </summary>
public sealed record ReferenceUiPattern
{
    public required string PatternId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string LayoutId { get; init; }
    public IList<ReferenceRegion> Regions { get; init; } = new List<ReferenceRegion>();
    public string? SourceApp { get; init; }
    public string? SourceVersion { get; init; }
}
