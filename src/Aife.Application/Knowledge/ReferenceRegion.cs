namespace Aife.Application.Knowledge;

/// <summary>
/// One expected element per layout slot in a reference UI pattern.
/// </summary>
public sealed record ReferenceRegion
{
    public required string Slot { get; init; }
    public required string Element { get; init; }
}
