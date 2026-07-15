using Aife.Application.Knowledge;
using FluentAssertions;
using Newtonsoft.Json;
using Xunit;

namespace Aife.Knowledge.Tests;

/// <summary>
/// Covers the deterministic (no-LLM) Phase 2 matching/scoring services that
/// back the MCP <c>match_element</c>, <c>check_token_conformance</c>, and
/// <c>score_prototype</c> tools, using a small self-contained fixture KB.
/// </summary>
public sealed class ComponentMatchingServiceTests : IDisposable
{
    private readonly string _knowledgeRoot;
    private readonly JsonKnowledgeProvider _provider;

    public ComponentMatchingServiceTests()
    {
        _knowledgeRoot = Path.Combine(Path.GetTempPath(), "aife-matching-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_knowledgeRoot, "components"));
        Directory.CreateDirectory(Path.Combine(_knowledgeRoot, "tokens"));

        WriteComponent("BUSButton", category: "button", mapsFromHtml: new[] { "button" },
            accessibility: new { role = "button", keyboardSupport = true, ariaProps = new[] { "aria-pressed" } });
        WriteComponent("BUSGrid", category: "table", mapsFromHtml: new[] { "table" }, accessibility: null);

        File.WriteAllText(Path.Combine(_knowledgeRoot, "tokens", "tokens.json"), """
            {
              "tokens": [
                { "name": "color.primary", "value": "#1548be", "category": "color" },
                { "name": "spacing.md", "value": "16px", "category": "spacing" }
              ]
            }
            """);

        File.WriteAllText(Path.Combine(_knowledgeRoot, "manifest.json"), """
            {
              "version": "test",
              "components": ["components/BUSButton.json", "components/BUSGrid.json"],
              "tokens": "tokens/tokens.json"
            }
            """);

        _provider = new JsonKnowledgeProvider(_knowledgeRoot);
    }

    private void WriteComponent(string componentId, string category, string[] mapsFromHtml, object? accessibility)
    {
        var component = new
        {
            componentId,
            name = componentId,
            category,
            status = "approved",
            description = $"{componentId} description",
            props = Array.Empty<object>(),
            mapsFromHtml,
            accessibility,
            importPath = $"Components/CustomUIs/{componentId}/{componentId}",
            exportName = componentId,
            isDefaultExport = false
        };

        File.WriteAllText(
            Path.Combine(_knowledgeRoot, "components", $"{componentId}.json"),
            JsonConvert.SerializeObject(component, Formatting.Indented));
    }

    public void Dispose()
    {
        try { Directory.Delete(_knowledgeRoot, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task MatchElementAsync_ExactMapping_ReturnsHighConfidenceMatch()
    {
        var service = new ComponentMatchingService(_provider);

        var matches = await service.MatchElementAsync("button", text: null, CancellationToken.None);

        matches.Should().ContainSingle();
        matches[0].ComponentId.Should().Be("BUSButton");
        matches[0].Confidence.Should().Be(1.0);
        matches[0].ImportPath.Should().Be("Components/CustomUIs/BUSButton/BUSButton");
        matches[0].ExportName.Should().Be("BUSButton");
    }

    [Fact]
    public async Task MatchElementAsync_TextSimilarity_BoostsConfidenceButCapsAtOne()
    {
        var service = new ComponentMatchingService(_provider);

        var matches = await service.MatchElementAsync("button", text: "BUSButton", CancellationToken.None);

        matches[0].Confidence.Should().Be(1.0); // already 1.0 baseline, boost caps at 1.0
        matches[0].Reason.Should().Contain("boost");
    }

    [Fact]
    public async Task MatchElementAsync_UnknownKind_ReturnsEmptyList()
    {
        var service = new ComponentMatchingService(_provider);

        var matches = await service.MatchElementAsync("carousel", text: null, CancellationToken.None);

        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckAsync_DetectsHardcodedColorAndSpacingViolations()
    {
        var checker = new TokenConformanceChecker(_provider);

        var result = await checker.CheckAsync(
            css: ".foo { color: #ff00aa; margin: 24px; padding: 16px; }",
            CancellationToken.None);

        // #ff00aa is not the token color; 24px margin isn't the spacing token (16px), 16px padding IS the token.
        result.Violations.Should().Contain(v => v.RuleId == "CONF_COLOR_OFF_TOKEN" && v.Value == "#ff00aa");
        result.Violations.Should().Contain(v => v.RuleId == "CONF_SPACING_OFF_TOKEN" && v.Value == "24px");
        result.Violations.Should().NotContain(v => v.Value == "16px");
        result.Score.Should().BeLessThan(100);
    }

    [Fact]
    public async Task CheckAsync_NoViolations_ReturnsPerfectScore()
    {
        var checker = new TokenConformanceChecker(_provider);

        var result = await checker.CheckAsync(css: ".foo { padding: 16px; }", CancellationToken.None);

        result.Violations.Should().BeEmpty();
        result.Score.Should().Be(100);
    }

    [Fact]
    public async Task ScoreAsync_EqualWeightsMatchTokenAndA11yScores()
    {
        var scorer = new PrototypeScorer(_provider);

        var elements = new List<ScoredElementInput>
        {
            new() { Kind = "button", MatchedComponentId = "BUSButton", Confidence = 1.0 }, // has a11y metadata
            new() { Kind = "table", MatchedComponentId = "BUSGrid", Confidence = 1.0 },      // no a11y metadata
            new() { Kind = "carousel", MatchedComponentId = null, Confidence = 0.0 }         // unmatched
        };
        var tokenViolations = new List<TokenViolation>
        {
            new() { RuleId = "CONF_COLOR_OFF_TOKEN", Value = "#ff00aa", Message = "bad color" }
        };

        var result = await scorer.ScoreAsync(elements, tokenViolations, CancellationToken.None);

        result.MatchScore.Should().BeApproximately(200.0 / 3, 0.1); // 2 of 3 matched
        result.TokenScore.Should().Be(90); // 1 violation => 100 - 10
        result.A11yScore.Should().Be(50); // 1 of 2 matched components has a11y metadata
        result.FinalScore.Should().BeApproximately((result.MatchScore + 90 + 50) / 3.0, 0.1);
        result.UnmatchedElements.Should().Contain("carousel");
        result.Suggestions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ScoreAsync_NoElements_ReturnsPerfectMatchScore()
    {
        var scorer = new PrototypeScorer(_provider);

        var result = await scorer.ScoreAsync(
            new List<ScoredElementInput>(), new List<TokenViolation>(), CancellationToken.None);

        result.MatchScore.Should().Be(100);
        result.TokenScore.Should().Be(100);
        result.A11yScore.Should().Be(100);
        result.FinalScore.Should().Be(100);
    }
}
