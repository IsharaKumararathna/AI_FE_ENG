using Aife.Application.AI;
using Aife.Application.AI.Stages;
using Aife.Application.Prompting;
using Aife.Domain.Generation;
using Newtonsoft.Json;

namespace Aife.Ai.Stages;

/// <summary>
/// Detects layout regions and UI primitives in a prototype by calling the LLM
/// Router with the <c>prototype.analyzer</c> prompt.
/// </summary>
public sealed class PrototypeAnalyzer : IPrototypeAnalyzer
{
    private readonly LlmRouter _router;
    private readonly IPromptManager _promptManager;

    public PrototypeAnalyzer(LlmRouter router, IPromptManager promptManager)
    {
        _router = router;
        _promptManager = promptManager;
    }

    public async Task<PrototypeAnalysis> AnalyzeAsync(Prototype prototype, CancellationToken ct)
    {
        var prompt = await _promptManager.GetPromptAsync(
            "prototype.analyzer",
            new Dictionary<string, string>
            {
                { "html", prototype.Html },
                { "css", prototype.Css }
            },
            ct);

        // Truncate inputs to fit within DeepSeek's context window (~8K tokens)
        var htmlTrimmed = prototype.Html.Length > 2000
            ? prototype.Html[..2000] + "\n... (truncated)"
            : prototype.Html;
        var cssTrimmed = prototype.Css.Length > 1000
            ? prototype.Css[..1000]
            : prototype.Css;

        var request = new LlmRequest
        {
            SystemMessage = prompt.SystemMessage + " Return ONLY valid JSON, no explanations.",
            UserMessage = $"HTML:\n{htmlTrimmed}\n\nCSS:\n{cssTrimmed}",
            JsonMode = true,
            StageContext = new StageContext { StageName = "Analyze" }
        };

        var response = await _router.CompleteAsync(request, ct);

        var text = response.Text;

        // DeepSeek often prepends explanations like "Here is the JSON..."
        // or wraps in ``` fences. Extract just the JSON portion.
        var jsonStart = text.IndexOfAny(new[] { '[', '{' });
        if (jsonStart > 0)
            text = text[jsonStart..];

        // Remove trailing ``` fences or natural text after JSON closes
        text = TrimAfterJsonClose(text);

        Console.WriteLine($"[Analyzer] LLM response ({response.Text.Length} raw, {text.Length} extracted): {text[..Math.Min(text.Length, 200)]}");

        var analysis = JsonConvert.DeserializeObject<PrototypeAnalysis>(text)
            ?? throw new InvalidOperationException("Failed to deserialize PrototypeAnalysis from LLM response.");

        return analysis;
    }

    private static string TrimAfterJsonClose(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
            return trimmed;

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            if (escaped) { escaped = false; continue; }
            if (ch == '\\') { escaped = true; continue; }
            if (ch == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (ch == '{' || ch == '[') depth++;
            else if (ch == '}' || ch == ']') { depth--; if (depth == 0) return trimmed[..(i + 1)]; }
        }

        return trimmed;
    }
}
