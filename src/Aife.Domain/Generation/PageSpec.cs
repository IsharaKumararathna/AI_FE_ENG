namespace Aife.Domain.Generation;

/// <summary>
/// One page specification within a PrototypeRequest.
/// </summary>
public sealed class PageSpec
{
    public required string Name { get; init; }
    public required string Layout { get; init; }
    public string? ReferencePattern { get; init; }
    public IList<RegionSpec> Regions { get; init; } = new List<RegionSpec>();
    public IList<ActionSpec> Actions { get; init; } = new List<ActionSpec>();
}
