namespace Aife.Domain.Generation;

/// <summary>
/// An intent spec (pages, regions, components, content) used by the Prototype
/// Generator to produce a conformant Prototype (ADR-006).
/// </summary>
public sealed class PrototypeRequest
{
    public required string Intent { get; init; }
    public IList<PageSpec> Pages { get; init; } = new List<PageSpec>();
}
