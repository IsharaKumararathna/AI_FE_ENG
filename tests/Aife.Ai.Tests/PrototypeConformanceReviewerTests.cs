using Aife.Application.AI;
using Aife.Application.AI.Stages;
using Aife.Application.Knowledge;
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

public class PrototypeConformanceReviewerTests
{
    private readonly IKnowledgeProvider _knowledge =
        new JsonKnowledgeProvider(AiTestPaths.Knowledge);

    private static (LlmRouter router, Mock<IPromptManager> promptManager) CreateStubs()
    {
        var fake = new StageAwareFakeProvider(new Dictionary<string, string>
        {
            ["ConformanceReview"] = """{"prototypeId":"p","outcome":0,"findings":[],"suggestions":[]}"""
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
                SystemMessage = "You review a prototype for drift.",
                Content = "You review a prototype for drift."
            });

        return (router, promptManager);
    }

    [Fact]
    public async Task Hardcoded_color_produces_token_conformance_finding()
    {
        var (router, promptManager) = CreateStubs();
        var reviewer = new PrototypeConformanceReviewer(router, promptManager.Object, _knowledge);

        var prototype = new Prototype
        {
            Id = "p-drift-001",
            Html = """<div style="background: #123456"><button>OK</button></div>""",
            Css = ".x { color: #abcdef; }"
        };
        var analysis = new PrototypeAnalysis
        {
            Layout = "AppLayout",
            Elements = new List<DetectedElement> { new() { Kind = "button" } }
        };

        var report = await reviewer.ReviewAsync(prototype, analysis, CancellationToken.None);

        report.Findings.Should().NotBeEmpty();
        report.Findings.Should().Contain(f =>
            f.RuleId == "CONF_COLOR_OFF_TOKEN" &&
            f.Category == "Token conformance");
        report.Outcome.Should().Be(ReviewOutcome.PassedWithWarnings);
    }

    [Fact]
    public async Task Unmapped_element_produces_component_conformance_finding()
    {
        var (router, promptManager) = CreateStubs();
        var reviewer = new PrototypeConformanceReviewer(router, promptManager.Object, _knowledge);

        var prototype = new Prototype
        {
            Id = "p-drift-002",
            Html = "<carousel></carousel>",
            Css = ""
        };
        var analysis = new PrototypeAnalysis
        {
            Layout = "AppLayout",
            Elements = new List<DetectedElement> { new() { Kind = "carousel" } }
        };

        var report = await reviewer.ReviewAsync(prototype, analysis, CancellationToken.None);

        report.Findings.Should().Contain(f =>
            f.RuleId == "CONF_UNMAPPED_ELEMENT" &&
            f.Category == "Component conformance");
    }

    [Fact]
    public async Task Conformant_prototype_produces_no_findings()
    {
        var (router, promptManager) = CreateStubs();
        var reviewer = new PrototypeConformanceReviewer(router, promptManager.Object, _knowledge);

        var prototype = new Prototype
        {
            Id = "p-clean-001",
            Html = "<button>Submit</button><table></table>",
            Css = ""
        };
        var analysis = new PrototypeAnalysis
        {
            Layout = "AppLayout",
            Elements = new List<DetectedElement>
            {
                new() { Kind = "button" },
                new() { Kind = "table" }
            }
        };

        var report = await reviewer.ReviewAsync(prototype, analysis, CancellationToken.None);

        report.Findings.Should().BeEmpty();
        report.Outcome.Should().Be(ReviewOutcome.Passed);
    }

    [Fact]
    public async Task Unapproved_layout_produces_layout_finding()
    {
        var (router, promptManager) = CreateStubs();
        var reviewer = new PrototypeConformanceReviewer(router, promptManager.Object, _knowledge);

        var prototype = new Prototype
        {
            Id = "p-drift-003",
            Html = "<button>OK</button>",
            Css = ""
        };
        var analysis = new PrototypeAnalysis
        {
            Layout = "NonExistentLayout",
            Elements = new List<DetectedElement> { new() { Kind = "button" } }
        };

        var report = await reviewer.ReviewAsync(prototype, analysis, CancellationToken.None);

        report.Findings.Should().Contain(f =>
            f.RuleId == "CONF_LAYOUT_NOT_APPROVED" &&
            f.Category == "Layout conformance");
    }

    [Fact]
    public async Task Report_includes_reference_ui_pattern_suggestion()
    {
        var (router, promptManager) = CreateStubs();
        var reviewer = new PrototypeConformanceReviewer(router, promptManager.Object, _knowledge);

        var prototype = new Prototype
        {
            Id = "p-suggest-001",
            Html = "<button>OK</button>",
            Css = ""
        };
        var analysis = new PrototypeAnalysis
        {
            Layout = "AppLayout",
            Elements = new List<DetectedElement> { new() { Kind = "button" } }
        };

        var report = await reviewer.ReviewAsync(prototype, analysis, CancellationToken.None);

        report.Suggestions.Should().Contain(s => s.Contains("ActiveInspectionsPage"));
    }
}
