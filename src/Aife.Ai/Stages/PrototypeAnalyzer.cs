using Aife.Application.AI;
using Aife.Application.AI.Stages;
using Aife.Application.Prompting;
using Aife.Domain.Generation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

        // Truncate aggressively — DeepSeek has 8K output limit. Ask for summary, not per-element detail.
        var htmlTrimmed = prototype.Html.Length > 2000
            ? prototype.Html[..2000]
            : prototype.Html;

        var request = new LlmRequest
        {
            SystemMessage = prompt.SystemMessage,
            UserMessage = $"List the DISTINCT UI element TYPES found (not every instance) — e.g. \"table\", \"button\", \"search input\". Return ONLY {{\"layout\":\"AppLayout\",\"elements\":[...]}}: {htmlTrimmed}",
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

        // DeepSeek returns layout as a complex object; normalize to a string.
        var raw = JObject.Parse(text);
        var layout = raw["layout"] is JObject layoutObj
            ? (layoutObj["type"]?.ToString() ?? "AppLayout")
            : raw["layout"]?.ToString() ?? "AppLayout";

        var elements = raw["elements"]?.ToObject<List<DetectedElement>>()
            ?? new List<DetectedElement>();

        return new PrototypeAnalysis { Layout = layout, Elements = elements };
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
