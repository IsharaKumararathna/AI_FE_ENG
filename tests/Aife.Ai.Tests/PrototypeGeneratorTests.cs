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
using Xunit;

namespace Aife.Ai.Tests;

public class PrototypeGeneratorTests
{
    private readonly IKnowledgeProvider _knowledge =
        new JsonKnowledgeProvider(AiTestPaths.Knowledge);

    private static (LlmRouter router, Mock<IPromptManager> promptManager) CreateStubs()
    {
        var fake = new StageAwareFakeProvider(new Dictionary<string, string>
        {
            ["GeneratePrototype"] = """
            {
              "html": "<html><body><button>Submit</button><table></table></body></html>",
              "css": ":root { --color-action-primary: #0066cc; }",
              "intermediateUiTree": {
                "page": "Dashboard",
                "layout": "AppLayout",
                "children": [
                  { "componentId": "PrimaryButton" },
                  { "componentId": "DataTable" }
                ]
              },
              "tokensUsed": ["color.action.primary", "spacing.button.padding"],
              "componentsUsed": ["PrimaryButton", "DataTable"]
            }
            """
        });
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
                SystemMessage = "You generate a DS-conformant prototype.",
                Content = "You generate a DS-conformant prototype."
            });

        return (router, promptManager);
    }

    [Fact]
    public async Task GenerateAsync_produces_conformant_prototype_with_tree()
    {
        var (router, promptManager) = CreateStubs();
        var generator = new PrototypeGenerator(router, promptManager.Object, _knowledge);

        var request = new PrototypeRequest
        {
            Intent = "A customer dashboard with a table and a button.",
            Pages = new List<PageSpec>
            {
                new()
                {
                    Name = "Dashboard",
                    Layout = "AppLayout",
                    Regions = new List<RegionSpec>
                    {
                        new() { Slot = "main", Component = "DataTable" }
                    },
                    Actions = new List<ActionSpec>
                    {
                        new() { Slot = "main", Component = "PrimaryButton", Label = "Add Customer" }
                    }
                }
            }
        };

        var result = await generator.GenerateAsync(request, CancellationToken.None);

        // Prototype
        result.Prototype.Should().NotBeNull();
        result.Prototype.Html.Should().NotBeEmpty();
        result.Prototype.Css.Should().NotBeEmpty();
        result.Prototype.Id.Should().StartWith("p-gen-");

        // Tree
        result.Tree.Should().NotBeNull();
        result.Tree.Page.Should().Be("Dashboard");
        result.Tree.Layout.Should().Be("AppLayout");
        result.Tree.Children.Should().HaveCount(2);
        result.Tree.Children.Should().Contain(n => n.ComponentId == "PrimaryButton");
        result.Tree.Children.Should().Contain(n => n.ComponentId == "DataTable");

        // Provenance
        result.TokensUsed.Should().Contain("color.action.primary");
        result.ComponentsUsed.Should().Contain("PrimaryButton");
        result.ComponentsUsed.Should().Contain("DataTable");
    }

    [Fact]
    public async Task GenerateAsync_rejects_unapproved_component_in_request()
    {
        var (router, promptManager) = CreateStubs();
        var generator = new PrototypeGenerator(router, promptManager.Object, _knowledge);

        var request = new PrototypeRequest
        {
            Intent = "A page with a non-existent component.",
            Pages = new List<PageSpec>
            {
                new()
                {
                    Name = "Test",
                    Layout = "AppLayout",
                    Regions = new List<RegionSpec>
                    {
                        new() { Slot = "main", Component = "NonExistentComponent" }
                    }
                }
            }
        };

        var act = async () => await generator.GenerateAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*NonExistentComponent*not approved*");
    }
}
