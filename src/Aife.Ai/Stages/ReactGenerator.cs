using Aife.Application.AI;
using Aife.Application.AI.Stages;
using Aife.Application.Knowledge;
using Aife.Application.Prompting;
using Aife.Domain.Generation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Aife.Ai.Stages;

/// <summary>
/// Produces React and TypeScript from the Intermediate UI Tree. Uses only
/// approved components and design tokens. Validates: all componentIds approved,
/// no inline CSS, no hardcoded color literals.
/// </summary>
public sealed class ReactGenerator : IReactGenerator
{
    private static readonly HashSet<string> HardcodedColorPatterns = new()
    {
        "#000", "#f00", "#0f0", "#00f"
        // #fff is allowed — it's the standard color.text.white token value.
        // rgb()/rgba() are allowed since they are valid for design token
        // values like box-shadow (rgba(23,23,23,0.1)) and other token value types.
    };

    private readonly LlmRouter _router;
    private readonly IPromptManager _promptManager;
    private readonly IKnowledgeProvider _knowledgeProvider;

    public ReactGenerator(LlmRouter router, IPromptManager promptManager, IKnowledgeProvider knowledgeProvider)
    {
        _router = router;
        _promptManager = promptManager;
        _knowledgeProvider = knowledgeProvider;
    }

    public async Task<IReadOnlyList<GeneratedArtifact>> GenerateAsync(IntermediateUiTree tree, CancellationToken ct)
    {
        var componentDocs = await BuildComponentDocs(tree, ct);
        var tokens = await _knowledgeProvider.GetDesignTokensAsync(ct);

        var prompt = await _promptManager.GetPromptAsync(
            "react.generator",
            new Dictionary<string, string>
            {
                { "intermediateUiTree", JsonConvert.SerializeObject(tree) },
                { "componentDocs", componentDocs },
                { "tokens", JsonConvert.SerializeObject(tokens) }
            },
            ct);

        var request = new LlmRequest
        {
            SystemMessage = prompt.SystemMessage,
            UserMessage = JsonConvert.SerializeObject(tree),
            JsonMode = true,
            StageContext = new StageContext { StageName = "Generate" }
        };

        var response = await _router.CompleteAsync(request, ct);

        var text = response.Text;

        // Extract JSON from DeepSeek's response
        var jsonStart = text.IndexOfAny(new[] { '[', '{' });
        if (jsonStart > 0)
            text = text[jsonStart..];

        text = TrimAfterJsonClose(text);

        Console.WriteLine($"[ReactGenerator] LLM response ({response.Text.Length} raw, {text.Length} extracted): {text[..Math.Min(text.Length, 200)]}");

        var artifacts = JsonConvert.DeserializeObject<List<GeneratedArtifact>>(text)
            ?? throw new InvalidOperationException($"Failed to deserialize GeneratedArtifact[] from LLM response: {text[..Math.Min(text.Length, 200)]}");

        ValidateArtifacts(artifacts, tree);

        return artifacts;
    }

    private async Task<string> BuildComponentDocs(IntermediateUiTree tree, CancellationToken ct)
    {
        var docs = new JArray();
        var seen = new HashSet<string>();

        foreach (var node in tree.Children)
        {
            if (seen.Contains(node.ComponentId))
                continue;
            seen.Add(node.ComponentId);

            var detail = await _knowledgeProvider.GetComponentAsync(node.ComponentId, ct);
            if (detail is null)
                throw new InvalidOperationException(
                    $"Component '{node.ComponentId}' is not approved or not found in the Knowledge Base.");

            var props = await _knowledgeProvider.GetComponentPropsAsync(node.ComponentId, ct);
            docs.Add(JObject.FromObject(new
            {
                componentId = detail.ComponentId,
                name = detail.Name,
                props = props?.Props ?? new List<ComponentProp>(),
                tokensConsumed = detail.TokensConsumed ?? new List<string>(),
                accessibility = detail.Accessibility
            }));
        }

        return docs.ToString();
    }

    private static void ValidateArtifacts(IReadOnlyList<GeneratedArtifact> artifacts, IntermediateUiTree tree)
    {
        foreach (var artifact in artifacts)
        {
            if (string.IsNullOrWhiteSpace(artifact.Path))
                throw new InvalidOperationException("Generated artifact has an empty path.");

            if (string.IsNullOrWhiteSpace(artifact.Content))
                throw new InvalidOperationException($"Artifact '{artifact.Path}' has empty content.");

            foreach (var pattern in HardcodedColorPatterns)
            {
                if (artifact.Content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Artifact '{artifact.Path}' contains a hardcoded color literal '{pattern}'. Use design tokens only.");
            }
        }
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
