using Aife.Application.AI;

namespace Aife.Infrastructure.AI;

/// <summary>
/// A simple LLM provider for MVP development and testing. Returns canned
/// responses based on the stage name in the request context. Real provider
/// clients replace this in the composition root when API keys are configured.
/// </summary>
public sealed class StubLlmProvider : ILlmProvider
{
    private static readonly Dictionary<string, string> StageResponses = new()
    {
        ["Analyze"] = """
        {
          "layout": "AppLayout",
          "elements": [
            { "kind": "button", "text": "Submit" },
            { "kind": "table", "text": "Data" }
          ]
        }
        """,
        ["Generate"] = """
        [
          {
            "path": ".aife/react/Dashboard.tsx",
            "content": "import { BUSButton, DataGrid } from '@org/ds/react'; export const Dashboard = () => null;"
          }
        ]
        """,
        ["Review"] = """
        {
          "score": 85,
          "outcome": 1,
          "violations": [],
          "suggestions": ["Consider adding a loading state."]
        }
        """,
        ["ConformanceReview"] = """
        {
          "prototypeId": "p-stub",
          "outcome": 0,
          "findings": [],
          "suggestions": []
        }
        """,
        ["GeneratePrototype"] = """
        {
          "html": "<html><body><button>Submit</button><table></table></body></html>",
          "css": ":root { --color-primary: #1548be; }",
          "intermediateUiTree": { "page": "Dashboard", "layout": "AppLayout", "children": [] },
          "tokensUsed": ["color.primary"],
          "componentsUsed": ["BUSButton"]
        }
        """
    };

    private readonly Func<LlmRequest, string> _responseFactory;

    public StubLlmProvider(string name = "stub", int priority = 100, string cannedResponse = "{}")
        : this(name, priority, _ => cannedResponse)
    {
    }

    public StubLlmProvider(string name, int priority, Func<LlmRequest, string> responseFactory)
    {
        Info = new LlmProviderInfo
        {
            Name = name,
            Priority = priority,
            Capabilities = new List<string> { "text", "json" }
        };
        _responseFactory = responseFactory;
    }

    public LlmProviderInfo Info { get; }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var stageName = request.StageContext?.StageName;
        var text = stageName is not null && StageResponses.TryGetValue(stageName, out var canned)
            ? canned
            : _responseFactory(request);

        var response = new LlmResponse
        {
            Text = text,
            Usage = new LlmUsage
            {
                PromptTokens = request.SystemMessage.Length / 4,
                CompletionTokens = text.Length / 4,
                TotalTokens = (request.SystemMessage.Length + text.Length) / 4
            },
            FinishReason = "stop"
        };

        return Task.FromResult(response);
    }
}
