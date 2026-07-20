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
            // Replace all real LLM providers with a single test fake
            var toRemove = services.Where(d =>
                d.ServiceType == typeof(ILlmProvider) ||
                d.ServiceType == typeof(IEnumerable<ILlmProvider>) ||
                d.ServiceType == typeof(LlmRouter)).ToList();
            foreach (var d in toRemove)
                services.Remove(d);

            var fake = new StageAwareFakeProvider(new Dictionary<string, string>
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
                    "path": ".aife/react/Dashboard.tsx",
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
                  "css": ":root { --color-primary: #1548be; }",
                  "intermediateUiTree": {
                    "page": "Dashboard",
                    "layout": "AppLayout",
                    "children": [
                      { "componentId": "BUSButton" },
                      { "componentId": "BUSGrid" }
                    ]
                  },
                  "tokensUsed": ["color.primary"],
                  "componentsUsed": ["BUSButton", "BUSGrid"]
                }
                """
            });

            services.AddSingleton<ILlmProvider>(fake);
            services.AddSingleton<IEnumerable<ILlmProvider>>(new[] { fake }.AsEnumerable());
            services.AddSingleton<LlmRouter>();
        });
    }
}
