namespace Aife.Application.Knowledge;

/// <summary>
/// Accessibility metadata for a Design System component.
/// </summary>
public sealed record ComponentAccessibility
{
    public string? Role { get; init; }
    public bool? KeyboardSupport { get; init; }
    public IList<string>? AriaProps { get; init; }
    public IList<string>? Notes { get; init; }
}
