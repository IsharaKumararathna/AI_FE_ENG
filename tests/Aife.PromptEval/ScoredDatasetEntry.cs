namespace Aife.PromptEval;

/// <summary>
/// One entry in a scored dataset: an input, the expected output, and a category.
/// </summary>
public sealed record ScoredDatasetEntry
{
    public required string Input { get; init; }
    public required string ExpectedOutput { get; init; }
    public required string Category { get; init; }
}
