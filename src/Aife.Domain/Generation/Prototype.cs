namespace Aife.Domain.Generation;

/// <summary>
/// An HTML and CSS artifact to be converted; uploaded by an author or generated
/// from intent (ADR-006).
/// </summary>
public sealed class Prototype
{
    public required string Id { get; init; }
    public required string Html { get; init; }
    public required string Css { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
