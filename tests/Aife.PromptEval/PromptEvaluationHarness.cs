namespace Aife.PromptEval;

/// <summary>
/// Runs a prompt version against a scored dataset and records accuracy, token
/// cost, and latency. A version below the threshold cannot become currentVersion.
///
/// This is the skeleton harness (E8-3). The real implementation will wire
/// IPromptManager + LlmRouter to run each entry through the actual pipeline.
/// For now, the caller provides a function that simulates the prompt + LLM call.
/// </summary>
public sealed class PromptEvaluationHarness
{
    private readonly Func<string, (string Response, int Tokens, long LatencyMs)> _runPrompt;

    public PromptEvaluationHarness(Func<string, (string Response, int Tokens, long LatencyMs)> runPrompt)
    {
        _runPrompt = runPrompt ?? throw new ArgumentNullException(nameof(runPrompt));
    }

    public PromptEvaluationResult Evaluate(
        string promptKey,
        string version,
        IReadOnlyList<ScoredDatasetEntry> dataset,
        double threshold = 0.80)
    {
        var correct = 0;
        var totalTokens = 0;
        var totalLatencyMs = 0L;

        foreach (var entry in dataset)
        {
            var (response, tokens, latencyMs) = _runPrompt(entry.Input);
            totalTokens += tokens;
            totalLatencyMs += latencyMs;

            if (response.Trim().Equals(entry.ExpectedOutput.Trim(), StringComparison.OrdinalIgnoreCase))
                correct++;
        }

        var count = dataset.Count;
        return new PromptEvaluationResult
        {
            PromptKey = promptKey,
            Version = version,
            TotalEntries = count,
            CorrectCount = correct,
            AverageTokenCost = count > 0 ? (double)totalTokens / count : 0,
            AverageLatencyMs = count > 0 ? (double)totalLatencyMs / count : 0,
            Threshold = threshold
        };
    }
}
