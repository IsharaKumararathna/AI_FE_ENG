namespace Aife.Application.Knowledge;

/// <summary>
/// Lightweight component representation for search results.
/// </summary>
public sealed record ComponentSummary
{
    public required string ComponentId { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Status { get; init; }
}
