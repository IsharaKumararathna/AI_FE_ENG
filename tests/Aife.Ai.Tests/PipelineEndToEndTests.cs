using Aife.Application.AI;
using Aife.Application.AI.Stages;
using Aife.Application.Knowledge;
using Aife.Application.Persistence;
using Aife.Application.Pipeline;
using Aife.Application.Prompting;
using Aife.Ai.Stages;
using Aife.Ai.Tests.TestHelpers;
using Aife.Domain.Enums;
using Aife.Domain.Generation;
using Aife.Knowledge;
using FluentAssertions;
using Moq;
using Xunit;

namespace Aife.Ai.Tests;

public class PipelineEndToEndTests
{
    [Fact]
    public async Task Full_pipeline_produces_react_and_review()
    {
        // ── Arrange: real knowledge provider with sample dataset ──
        var knowledge = new JsonKnowledgeProvider(AiTestPaths.Knowledge);

        // ── Stub LLM returning canned JSON per stage ──
        var fakeProvider = new StageAwareFakeProvider(new Dictionary<string, string>
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
              "score": 85,
              "outcome": 1,
              "violations": [],
              "suggestions": ["Consider adding a loading state."]
            }
            """
        });
        var router = new LlmRouter(new[] { fakeProvider });

        // ── Mocked prompt manager (prompt content irrelevant for stub LLM) ──
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

        // ── Real stages + assembler ──
        var analyzer = new PrototypeAnalyzer(router, promptManager.Object);
        var mapper = new ComponentMapper(router, promptManager.Object, knowledge);
        var assembler = new UiTreeAssembler(knowledge);
        var generator = new ReactGenerator(router, promptManager.Object, knowledge);
        var reviewer = new AiReviewer(router, promptManager.Object, knowledge);

        // ── Mocked repos ──
        var sessionRepo = new Mock<ISessionRepository>();
        sessionRepo
            .Setup(r => r.SaveAsync(It.IsAny<GenerationSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var artifactRepo = new Mock<IArtifactRepository>();
        artifactRepo
            .Setup(r => r.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<GeneratedArtifact>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new RunGenerationSessionHandler(
            analyzer, mapper, assembler, generator, reviewer,
            sessionRepo.Object, artifactRepo.Object);

        // ── Act ──
        var prototype = new Prototype
        {
            Id = "p-test-001",
            Html = "<button>Submit</button><table></table>",
            Css = ""
        };

        var result = await handler.HandleAsync(prototype, CancellationToken.None);

        // ── Assert: session completed ──
        result.Session.Status.Should().Be(SessionStatus.Completed);
        result.Session.Stages.Should().AllSatisfy(s =>
            s.Status.Should().Be(StageStatus.Completed));

        // ── Assert: analysis ──
        result.Analysis.Should().NotBeNull();
        result.Analysis!.Layout.Should().Be("AppLayout");
        result.Analysis.Elements.Should().HaveCount(2);

        // ── Assert: mappings (baseline, no LLM needed) ──
        result.Mappings.Should().NotBeNull();
        result.Mappings!.Should().Contain(m =>
            m.ComponentId == "BUSButton" && m.Confidence >= 0.95);
        result.Mappings.Should().Contain(m =>
            m.ComponentId == "BUSGrid" && m.Confidence >= 0.95);

        // ── Assert: intermediate UI tree ──
        result.Tree.Should().NotBeNull();
        result.Tree!.Layout.Should().Be("AppLayout");
        result.Tree.Children.Should().HaveCount(2);
        result.Tree.Children.Should().Contain(n => n.ComponentId == "BUSButton");
        result.Tree.Children.Should().Contain(n => n.ComponentId == "BUSGrid");

        // ── Assert: generated artifacts ──
        result.Artifacts.Should().NotBeEmpty();
        result.Artifacts!.Should().Contain(a => a.Path == ".aife/react/Dashboard.tsx");
        result.Artifacts.Should().Contain(a => a.Content.Contains("BUSButton"));

        // ── Assert: review report ──
        result.Review.Should().NotBeNull();
        result.Review!.Score.Should().Be(85);
        result.Review.Outcome.Should().Be(ReviewOutcome.PassedWithWarnings);
        result.Review.Suggestions.Should().Contain(s => s.Contains("loading state"));
    }
}
