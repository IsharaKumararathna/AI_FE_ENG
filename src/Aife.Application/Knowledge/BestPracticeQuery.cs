namespace Aife.Application.Knowledge;

/// <summary>
/// Query parameters for searching best practices.
/// </summary>
public sealed record BestPracticeQuery
{
    public IList<string>? Tags { get; init; }
}
