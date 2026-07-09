using Aife.Domain.Enums;

namespace Aife.Domain.Generation;

/// <summary>
/// The AI Reviewer output: score, violations, suggestions.
/// </summary>
public sealed class ReviewReport
{
    public int Score { get; init; }
    public ReviewOutcome Outcome { get; init; }
    public IList<Violation> Violations { get; init; } = new List<Violation>();
    public IList<string> Suggestions { get; init; } = new List<string>();
}
