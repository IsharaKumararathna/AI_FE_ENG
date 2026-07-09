namespace Aife.Application.Knowledge;

/// <summary>
/// Query parameters for searching Design System components.
/// </summary>
public sealed record ComponentQuery
{
    public string? Category { get; init; }
    public string? Status { get; init; } = "approved";
    public string? Text { get; init; }
}
