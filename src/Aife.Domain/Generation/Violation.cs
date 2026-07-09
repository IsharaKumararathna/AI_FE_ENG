using Aife.Domain.Enums;

namespace Aife.Domain.Generation;

/// <summary>
/// A single compliance, accessibility, or architecture finding.
/// </summary>
public sealed record Violation
{
    public required string RuleId { get; init; }
    public ViolationCategory Category { get; init; }
    public Severity Severity { get; init; }
    public required string Message { get; init; }
    public string? Location { get; init; }
}
