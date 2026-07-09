namespace Aife.Application.Knowledge;

/// <summary>
/// Query parameters for searching icons.
/// </summary>
public sealed record IconQuery
{
    public string? Name { get; init; }
}
