namespace Aife.Application.Knowledge;

/// <summary>
/// The prop definitions for a single component, returned by GetComponentPropsAsync.
/// </summary>
public sealed record ComponentProps
{
    public required string ComponentId { get; init; }
    public IList<ComponentProp> Props { get; init; } = new List<ComponentProp>();
}
