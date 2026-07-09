namespace Aife.Application.Knowledge;

/// <summary>
/// One design token (color, spacing, radius, typography, border, shadow).
/// </summary>
public sealed record DesignToken
{
    public required string Name { get; init; }
    public required string Value { get; init; }
    public required string Category { get; init; }
    public string? Description { get; init; }
}
