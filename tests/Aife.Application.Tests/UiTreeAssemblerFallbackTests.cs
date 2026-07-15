using Aife.Application.AI.Stages;
using Aife.Application.Knowledge;
using Aife.Domain.Generation;
using FluentAssertions;
using Moq;
using Xunit;

namespace Aife.Application.Tests;

/// <summary>
/// Covers <see cref="UiTreeAssembler"/>'s KB-driven fallback for elements the
/// primary mapper couldn't confidently place (Phase 5 hardening — this used
/// to hardcode project-specific BUS component IDs, which broke portability
/// across different projects' Knowledge Bases).
/// </summary>
public sealed class UiTreeAssemblerFallbackTests
{
    private static ComponentDetail MakeDetail(string id, string category, string[]? mapsFromHtml) => new()
    {
        ComponentId = id,
        Name = id,
        Category = category,
        Status = "approved",
        MapsFromHtml = mapsFromHtml
    };

    private static Mock<IKnowledgeProvider> CreateKnowledgeProviderMock(params ComponentDetail[] components)
    {
        var mock = new Mock<IKnowledgeProvider>();

        var summaries = components
            .Select(c => new ComponentSummary { ComponentId = c.ComponentId, Name = c.Name, Category = c.Category, Status = c.Status })
            .ToList();

        mock.Setup(p => p.SearchComponentsAsync(It.IsAny<ComponentQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ComponentSummary>)summaries);

        foreach (var component in components)
        {
            mock.Setup(p => p.GetComponentAsync(component.ComponentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(component);
        }

        return mock;
    }

    [Fact]
    public async Task AssembleAsync_UsesExactMapsFromHtmlMatch_ForLowConfidenceElement()
    {
        var providerMock = CreateKnowledgeProviderMock(
            MakeDetail("BUSButton", "button", new[] { "button" }),
            MakeDetail("BUSGrid", "table", new[] { "table" }));

        var assembler = new UiTreeAssembler(providerMock.Object);
        var analysis = new PrototypeAnalysis { Layout = "AppLayout", Elements = new List<DetectedElement>() };
        var mappings = new List<ComponentMapping>
        {
            new() { ElementRef = "table", ComponentId = null, Confidence = 0.0 }
        };

        var tree = await assembler.AssembleAsync(analysis, mappings, CancellationToken.None);

        tree.Children.Should().Contain(n => n.ComponentId == "BUSGrid");
    }

    [Fact]
    public async Task AssembleAsync_FallsBackToCategoryMatch_WhenNoExactMapsFromHtml()
    {
        // Neither component declares mapsFromHtml, so the exact-match branch
        // never fires; the shared ElementCategoryFallback table should still
        // resolve "checkbox" -> category "form" -> BUSCheckboxLike.
        var providerMock = CreateKnowledgeProviderMock(
            MakeDetail("BUSCheckboxLike", "form", mapsFromHtml: null),
            MakeDetail("BUSButtonLike", "button", mapsFromHtml: null));

        var assembler = new UiTreeAssembler(providerMock.Object);
        var analysis = new PrototypeAnalysis { Layout = "AppLayout", Elements = new List<DetectedElement>() };
        var mappings = new List<ComponentMapping>
        {
            new() { ElementRef = "checkbox", ComponentId = null, Confidence = 0.0 }
        };

        var tree = await assembler.AssembleAsync(analysis, mappings, CancellationToken.None);

        tree.Children.Should().Contain(n => n.ComponentId == "BUSCheckboxLike");
    }

    [Fact]
    public async Task AssembleAsync_FallsBackToAnyApprovedComponent_WhenNothingMatchesKindOrCategory()
    {
        // "carousel" isn't in the shared fallback table and nothing declares
        // mapsFromHtml for it — should still resolve to *some* real KB
        // component rather than a hardcoded, possibly-nonexistent ID.
        var providerMock = CreateKnowledgeProviderMock(
            MakeDetail("OnlyComponent", "other", mapsFromHtml: null));

        var assembler = new UiTreeAssembler(providerMock.Object);
        var analysis = new PrototypeAnalysis { Layout = "AppLayout", Elements = new List<DetectedElement>() };
        var mappings = new List<ComponentMapping>
        {
            new() { ElementRef = "carousel", ComponentId = null, Confidence = 0.0 }
        };

        var tree = await assembler.AssembleAsync(analysis, mappings, CancellationToken.None);

        tree.Children.Should().Contain(n => n.ComponentId == "OnlyComponent");
    }
}
