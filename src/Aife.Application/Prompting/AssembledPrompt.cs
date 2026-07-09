namespace Aife.Application.Prompting;

/// <summary>
/// The assembled prompt returned by the Prompt Manager, with variables filled
/// and version metadata for traceability.
/// </summary>
public sealed record AssembledPrompt
{
    public required string Key { get; init; }
    public required string Version { get; init; }
    public required string SystemMessage { get; init; }
    public required string Content { get; init; }
    public string? OutputContract { get; init; }
}
