namespace Aife.Domain.Generation;

/// <summary>
/// Structured result of analyzing a prototype: layout and detected elements.
/// <c>Layout</c> is a string in the domain model but may arrive as a complex
/// object from LLM providers (DeepSeek, etc.). The analyzer normalizes it.
/// </summary>
public sealed class PrototypeAnalysis
{
    public required string Layout { get; init; }
    public IList<DetectedElement> Elements { get; init; } = new List<DetectedElement>();
}
