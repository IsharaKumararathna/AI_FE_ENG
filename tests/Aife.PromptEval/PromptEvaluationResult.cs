namespace Aife.PromptEval;

/// <summary>
/// The result of evaluating a prompt version against a scored dataset.
/// A version below the threshold cannot become currentVersion (per the
/// prompt versioning doc).
/// </summary>
public sealed record PromptEvaluationResult
{
    public required string PromptKey { get; init; }
    public required string Version { get; init; }
    public int TotalEntries { get; init; }
    public int CorrectCount { get; init; }
    public double Accuracy => TotalEntries > 0 ? (double)CorrectCount / TotalEntries : 0;
    public double AverageTokenCost { get; init; }
    public double AverageLatencyMs { get; init; }
    public double Threshold { get; init; }
    public bool Passed => Accuracy >= Threshold;

    public override string ToString() =>
        $"Prompt '{PromptKey}' v{Version}: {CorrectCount}/{TotalEntries} correct " +
        $"(accuracy={Accuracy:P1}, avgTokens={AverageTokenCost:F0}, avgLatency={AverageLatencyMs:F0}ms, " +
        $"threshold={Threshold:P1}, {(Passed ? "PASSED" : "FAILED")})";
}
