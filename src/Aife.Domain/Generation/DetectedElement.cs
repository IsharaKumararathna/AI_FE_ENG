namespace Aife.Domain.Generation;

/// <summary>
/// A UI primitive found in a prototype (button, table, form, and so on).
/// </summary>
public sealed record DetectedElement
{
    public required string Kind { get; init; }
    public string? Text { get; init; }
    public string? Bounds { get; init; }
}
