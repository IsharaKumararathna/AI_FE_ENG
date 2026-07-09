namespace Aife.Domain.Prompting;

/// <summary>
/// A versioned prompt with variables and an output contract.
/// </summary>
public sealed class PromptTemplate
{
    public required string Key { get; init; }
    public string? CurrentVersion { get; set; }
}
