namespace Aife.Domain.Generation;

/// <summary>
/// A mapping from a detected element to an approved Design System component,
/// with confidence. A confidence below the threshold is flagged for human review.
/// </summary>
public sealed record ComponentMapping
{
    public required string ElementRef { get; init; }
    public string? ComponentId { get; init; }
    public double Confidence { get; init; }
    public string? Reason { get; init; }
}
