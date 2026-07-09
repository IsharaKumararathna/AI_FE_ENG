namespace Aife.Domain.Generation;

/// <summary>
/// A component placed in a layout slot within a page specification.
/// </summary>
public sealed record RegionSpec
{
    public required string Slot { get; init; }
    public required string Component { get; init; }
    public IDictionary<string, object?>? Props { get; init; }
}
