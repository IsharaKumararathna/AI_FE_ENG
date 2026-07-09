namespace Aife.Domain.Generation;

/// <summary>
/// Structured result of analyzing a prototype: layout and detected elements.
/// </summary>
public sealed class PrototypeAnalysis
{
    public required string Layout { get; init; }
    public IList<DetectedElement> Elements { get; init; } = new List<DetectedElement>();
}
