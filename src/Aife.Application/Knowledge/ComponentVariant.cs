namespace Aife.Application.Knowledge;

/// <summary>
/// A named variant of a Design System component with prop overrides.
/// </summary>
public sealed record ComponentVariant
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public IDictionary<string, object?>? PropOverrides { get; init; }
}
