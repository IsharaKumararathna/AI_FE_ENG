using Aife.Domain.Enums;

namespace Aife.Domain.Generation;

/// <summary>
/// A single drift finding in a conformance report: category, severity, message, location.
/// </summary>
public sealed record ConformanceFinding
{
    public required string RuleId { get; init; }
    public required string Category { get; init; }
    public Severity Severity { get; init; } = Severity.Advisory;
    public required string Message { get; init; }
    public string? Location { get; init; }
}
