using Aife.Application.AI;
using Aife.Application.AI.Stages;
using Aife.Application.Knowledge;
using Aife.Application.Prompting;
using Aife.Ai.Stages;
using Aife.Ai.Tests.TestHelpers;
using Aife.Domain.Generation;
using Aife.Knowledge;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace Aife.Ai.Tests;

public class GoldenFileTests
{
    private readonly IKnowledgeProvider _knowledge =
        new JsonKnowledgeProvider(AiTestPaths.Knowledge);

    private static (LlmRouter router, Mock<IPromptManager> promptManager) CreateStubs(
        Dictionary<string, string> responses)
    {
        var fake = new StageAwareFakeProvider(responses);
        var router = new LlmRouter(new[] { fake });

        var promptManager = new Mock<IPromptManager>();
        promptManager
            .Setup(pm => pm.GetPromptAsync(
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, IDictionary<string, string> _, CancellationToken _) => new AssembledPrompt
            {
                Key = key,
                Version = "1.0.0",
                SystemMessage = "You are an AI assistant.",
                Content = "You are an AI assistant."
            });

        return (router, promptManager);
    }

    [Fact]
    public async Task React_generator_output_matches_golden_file()
    {
        var cannedResponse = """
            [
              {
                "path": "src/Dashboard.tsx",
                "content": "import { BUSButton, DataGrid } from '@org/ds/react'; export const Dashboard = () => null;"
              }
            ]
            """;

        var (router, promptManager) = CreateStubs(new Dictionary<string, string>
        {
            ["Generate"] = cannedResponse
        });

        var generator = new ReactGenerator(router, promptManager.Object, _knowledge);

        var tree = new IntermediateUiTree
        {
            Page = "Dashboard",
            Layout = "AppLayout",
            Children = new List<UiNode>
            {
                new() { NodeId = "n1", ComponentId = "BUSButton" },
                new() { NodeId = "n2", ComponentId = "DataGrid" }
            }
        };

        var artifacts = await generator.GenerateAsync(tree, CancellationToken.None);
        var actualJson = JsonConvert.SerializeObject(artifacts, Formatting.Indented,
            new JsonSerializerSettings
            {
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
            });

        var goldenPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "GoldenFiles", "react-generator-output.json");
        var goldenJson = await File.ReadAllTextAsync(goldenPath);

        actualJson.Should().Be(goldenJson.Trim(),
            "React generation output must match the committed golden file. " +
            "If this change is intended, update the golden file after review.");
    }

    [Fact]
    public async Task Prototype_generator_output_matches_golden_file()
    {
        var cannedResponse = """
            {
              "html": "<html><body><button>Submit</button><table></table></body></html>",
              "css": ":root { --color-primary: #1548be; }",
              "intermediateUiTree": {
                "page": "Dashboard",
                "layout": "AppLayout",
                "children": [
                  { "componentId": "BUSButton" },
                  { "componentId": "DataGrid" }
                ]
              },
              "tokensUsed": ["color.primary", "spacing.md"],
              "componentsUsed": ["BUSButton", "DataGrid"]
            }
            """;

        var (router, promptManager) = CreateStubs(new Dictionary<string, string>
        {
            ["GeneratePrototype"] = cannedResponse
        });

        var generator = new PrototypeGenerator(router, promptManager.Object, _knowledge);

        var request = new PrototypeRequest
        {
            Intent = "A dashboard with a table and a button.",
            Pages = new List<PageSpec>
            {
                new()
                {
                    Name = "Dashboard",
                    Layout = "AppLayout",
                    Regions = new List<RegionSpec>
                    {
                        new() { Slot = "main", Component = "DataGrid" }
                    },
                    Actions = new List<ActionSpec>
                    {
                        new() { Slot = "main", Component = "BUSButton", Label = "Add" }
                    }
                }
            }
        };

        var result = await generator.GenerateAsync(request, CancellationToken.None);

        // Compare meaningful fields (ignore the generated prototype ID which is random)
        result.Prototype.Html.Should().Be("<html><body><button>Submit</button><table></table></body></html>");
        result.Prototype.Css.Should().Be(":root { --color-primary: #1548be; }");
        result.Tree.Page.Should().Be("Dashboard");
        result.Tree.Children.Should().HaveCount(2);
        result.TokensUsed.Should().Contain("color.primary");
        result.ComponentsUsed.Should().Contain("BUSButton");
    }
}
