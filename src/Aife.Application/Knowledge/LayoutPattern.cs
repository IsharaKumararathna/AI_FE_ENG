namespace Aife.Application.Knowledge;

/// <summary>
/// An approved layout pattern with named slots.
/// </summary>
public sealed record LayoutPattern
{
    public required string LayoutId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public IList<string> Slots { get; init; } = new List<string>();
}
