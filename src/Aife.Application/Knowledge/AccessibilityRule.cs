namespace Aife.Application.Knowledge;

/// <summary>
/// One accessibility rule from the Knowledge Base.
/// </summary>
public sealed record AccessibilityRule
{
    public required string RuleId { get; init; }
    public required string Category { get; init; }
    public required string Statement { get; init; }
    public required string Severity { get; init; }
}
