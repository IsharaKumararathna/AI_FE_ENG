namespace Aife.Domain.Generation;

/// <summary>
/// An interactive action (e.g. a button) placed in a layout slot within a page specification.
/// </summary>
public sealed record ActionSpec
{
    public required string Slot { get; init; }
    public required string Component { get; init; }
    public string? Label { get; init; }
}
