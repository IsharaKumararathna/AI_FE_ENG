using Aife.Application.AI;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Aife.Api.Tests;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public static string FindKnowledgePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "knowledge");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the knowledge/ directory.");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var knowledgePath = FindKnowledgePath();
        var dataPath = Path.Combine(Path.GetTempPath(), "aife-api-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataPath);

        builder.UseSetting("KnowledgePath", knowledgePath);
        builder.UseSetting("DataPath", dataPath);

        builder.ConfigureServices(services =>
        {
            // Replace the StubLlmProvider with a stage-aware fake
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ILlmProvider));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddSingleton<ILlmProvider>(new StageAwareFakeProvider(new Dictionary<string, string>
            {
                ["Analyze"] = """
                {
                  "layout": "AppLayout",
                  "elements": [
                    { "kind": "button", "text": "Submit" },
                    { "kind": "table", "text": "Customers" }
                  ]
                }
                """,
                ["Generate"] = """
                [
                  {
                    "path": "src/Dashboard.tsx",
                    "content": "import { BUSButton, BUSGrid } from '@org/ds/react'; export const Dashboard = () => null;"
                  }
                ]
                """,
                ["Review"] = """
                {
                  "score": 90,
                  "outcome": 0,
                  "violations": [],
                  "suggestions": []
                }
                """,
                ["ConformanceReview"] = """
                {
                  "prototypeId": "p-test",
                  "outcome": 0,
                  "findings": [],
                  "suggestions": []
                }
                """,
                ["GeneratePrototype"] = """
                {
                  "html": "<html><body><button>Submit</button><table></table></body></html>",
                  "css": ":root { --color-action-primary: #0066cc; }",
                  "intermediateUiTree": {
                    "page": "Dashboard",
                    "layout": "AppLayout",
                    "children": [
                      { "componentId": "BUSButton" },
                      { "componentId": "BUSGrid" }
                    ]
                  },
                  "tokensUsed": ["color.action.primary"],
                  "componentsUsed": ["BUSButton", "BUSGrid"]
                }
                """
            }));
        });
    }
}
