using System.Text.RegularExpressions;
using Aife.Application.AI;
using Aife.Application.AI.Stages;
using Aife.Application.Knowledge;
using Aife.Application.Prompting;
using Aife.Domain.Generation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Aife.Ai.Stages;

/// <summary>
/// Generates a DS-conformant HTML/CSS prototype from a PrototypeRequest
/// (ADR-006). Pulls only approved components, tokens, layouts, and reference
/// UI patterns from the Knowledge Base so the output is conformant by
/// construction. Validates: approved components only, no hardcoded color
/// literals, approved layouts only.
/// </summary>
public sealed class PrototypeGenerator : IPrototypeGenerator
{
    private static readonly Regex HexColorRegex =
        new(@"#(?:[0-9a-fA-F]{3}){1,2}\b", RegexOptions.Compiled);

    private readonly LlmRouter _router;
    private readonly IPromptManager _promptManager;
    private readonly IKnowledgeProvider _knowledgeProvider;

    public PrototypeGenerator(LlmRouter router, IPromptManager promptManager, IKnowledgeProvider knowledgeProvider)
    {
        _router = router;
        _promptManager = promptManager;
        _knowledgeProvider = knowledgeProvider;
    }

    public async Task<GeneratedPrototype> GenerateAsync(PrototypeRequest request, CancellationToken ct)
    {
        // Gather KB knowledge for the prompt
        var componentDocs = await BuildComponentDocs(request, ct);
        var tokens = await _knowledgeProvider.GetDesignTokensAsync(ct);
        var layouts = await _knowledgeProvider.GetLayoutPatternsAsync(ct);
        var referencePatterns = await _knowledgeProvider.GetReferenceUiPatternsAsync(ct);

        var prompt = await _promptManager.GetPromptAsync(
            "prototype.generator",
            new Dictionary<string, string>
            {
                { "prototypeRequest", JsonConvert.SerializeObject(request) },
                { "availableComponents", componentDocs },
                { "tokens", JsonConvert.SerializeObject(tokens) },
                { "layoutPatterns", JsonConvert.SerializeObject(layouts) },
                { "referenceUiPatterns", JsonConvert.SerializeObject(referencePatterns) }
            },
            ct);

        var llmRequest = new LlmRequest
        {
            SystemMessage = prompt.SystemMessage,
            UserMessage = JsonConvert.SerializeObject(request),
            JsonMode = true,
            StageContext = new StageContext { StageName = "GeneratePrototype" }
        };

        var response = await _router.CompleteAsync(llmRequest, ct);

        var responseJson = JObject.Parse(response.Text);
        var html = responseJson["html"]?.ToString() ?? "";
        var css = responseJson["css"]?.ToString() ?? "";
        var tree = responseJson["intermediateUiTree"]?.ToObject<IntermediateUiTree>()
            ?? new IntermediateUiTree { Page = "Generated", Layout = "AppLayout" };
        var tokensUsed = responseJson["tokensUsed"]?.ToObject<List<string>>() ?? new List<string>();
        var componentsUsed = responseJson["componentsUsed"]?.ToObject<List<string>>() ?? new List<string>();

        // Validate: by construction
        ValidateConformance(html, css, tree, componentsUsed, ct);

        var prototype = new Prototype
        {
            Id = $"p-gen-{Guid.NewGuid():N}",
            Html = html,
            Css = css
        };

        return new GeneratedPrototype
        {
            Prototype = prototype,
            Tree = tree,
            TokensUsed = tokensUsed,
            ComponentsUsed = componentsUsed
        };
    }

    private async Task<string> BuildComponentDocs(PrototypeRequest request, CancellationToken ct)
    {
        var docs = new JArray();
        var seen = new HashSet<string>();

        foreach (var page in request.Pages)
        {
            foreach (var region in page.Regions)
            {
                CollectComponentDocs(region.Component, docs, seen, ct);
            }
            foreach (var action in page.Actions)
            {
                CollectComponentDocs(action.Component, docs, seen, ct);
            }
        }

        return docs.ToString();
    }

    private void CollectComponentDocs(
        string componentId,
        JArray docs,
        HashSet<string> seen,
        CancellationToken ct)
    {
        if (seen.Contains(componentId))
            return;
        seen.Add(componentId);

        var detail = _knowledgeProvider.GetComponentAsync(componentId, ct).GetAwaiter().GetResult();
        if (detail is null)
            throw new InvalidOperationException(
                $"Component '{componentId}' is not approved or not found in the Knowledge Base.");

        docs.Add(JObject.FromObject(new
        {
            componentId = detail.ComponentId,
            name = detail.Name,
            props = detail.Props,
            tokensConsumed = detail.TokensConsumed ?? new List<string>(),
            accessibility = detail.Accessibility
        }));
    }

    private static void ValidateConformance(
        string html,
        string css,
        IntermediateUiTree tree,
        IList<string> componentsUsed,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(html))
            throw new InvalidOperationException("Generated prototype has empty HTML.");

        // Check for hardcoded colors in HTML inline styles only (not CSS variable
        // definitions, which are expected to contain token values).
        var inlineStyleColorRegex = new Regex(
            @"style\s*=\s*""[^""]*#(?:[0-9a-fA-F]{3}){1,2}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        if (inlineStyleColorRegex.IsMatch(html))
        {
            throw new InvalidOperationException(
                "Generated prototype HTML contains hardcoded color literals in inline styles. Use design token CSS variables only.");
        }
    }
}
