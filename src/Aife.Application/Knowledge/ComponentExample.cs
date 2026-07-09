namespace Aife.Application.Knowledge;

/// <summary>
/// A usage example for a Design System component.
/// </summary>
public sealed record ComponentExample
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Code { get; init; }
}
