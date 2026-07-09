namespace Aife.Application.Knowledge;

/// <summary>
/// One prop definition on a Design System component.
/// </summary>
public sealed record ComponentProp
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public bool Required { get; init; }
    public object? Default { get; init; }
    public string? Description { get; init; }
    public IList<string>? Enum { get; init; }
}
