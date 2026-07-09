namespace Aife.Application.Knowledge;

/// <summary>
/// One icon from the Design System.
/// </summary>
public sealed record Icon
{
    public required string Name { get; init; }
    public required string Label { get; init; }
    public required string SvgPath { get; init; }
}
