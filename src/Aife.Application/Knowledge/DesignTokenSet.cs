namespace Aife.Application.Knowledge;

/// <summary>
/// The full set of design tokens, returned by GetDesignTokensAsync.
/// </summary>
public sealed record DesignTokenSet
{
    public IList<DesignToken> Tokens { get; init; } = new List<DesignToken>();
}
