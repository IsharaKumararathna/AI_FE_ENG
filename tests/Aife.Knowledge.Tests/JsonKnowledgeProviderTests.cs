using Aife.Application.Knowledge;
using FluentAssertions;
using Xunit;

namespace Aife.Knowledge.Tests;

public class JsonKnowledgeProviderTests
{
    private readonly IKnowledgeProvider _provider =
        new JsonKnowledgeProvider(KnowledgeTestPaths.Knowledge);

    [Fact]
    public async Task SearchComponentsAsync_returns_approved_components()
    {
        var results = await _provider.SearchComponentsAsync(new ComponentQuery(), CancellationToken.None);

        results.Should().HaveCount(7);
        results.Should().Contain(c => c.ComponentId == "BUSButton");
        results.Should().Contain(c => c.ComponentId == "DataGrid");
    }

    [Fact]
    public async Task SearchComponentsAsync_filters_by_category()
    {
        var results = await _provider.SearchComponentsAsync(
            new ComponentQuery { Category = "button" }, CancellationToken.None);

        results.Should().HaveCount(1);
        results[0].ComponentId.Should().Be("BUSButton");
    }

    [Fact]
    public async Task GetComponentAsync_returns_full_detail_with_props()
    {
        var component = await _provider.GetComponentAsync("BUSButton", CancellationToken.None);

        component.Should().NotBeNull();
        component!.ComponentId.Should().Be("BUSButton");
        component.Props.Should().NotBeEmpty();
        component.Props.Should().Contain(p => p.Name == "children" && p.Required);
        component.MapsFromHtml.Should().Contain("button");
    }

    [Fact]
    public async Task GetComponentAsync_returns_null_for_unknown_id()
    {
        var component = await _provider.GetComponentAsync("NonExistent", CancellationToken.None);
        component.Should().BeNull();
    }

    [Fact]
    public async Task GetComponentPropsAsync_returns_props_for_component()
    {
        var props = await _provider.GetComponentPropsAsync("DataGrid", CancellationToken.None);

        props.Should().NotBeNull();
        props!.ComponentId.Should().Be("DataGrid");
        props.Props.Should().Contain(p => p.Name == "columns" && p.Required);
    }

    [Fact]
    public async Task GetDesignTokensAsync_returns_token_set()
    {
        var tokens = await _provider.GetDesignTokensAsync(CancellationToken.None);

        tokens.Tokens.Should().NotBeEmpty();
        tokens.Tokens.Should().Contain(t => t.Name == "color.primary");
        tokens.Tokens.Should().Contain(t => t.Name == "spacing.md");
    }

    [Fact]
    public async Task GetLayoutPatternsAsync_returns_approved_layouts()
    {
        var layouts = await _provider.GetLayoutPatternsAsync(CancellationToken.None);

        layouts.Should().HaveCount(1);
        layouts[0].LayoutId.Should().Be("AppLayout");
        layouts[0].Slots.Should().Contain(new[] { "header", "sidebar", "main", "footer" });
    }

    [Fact]
    public async Task GetReferenceUiPatternsAsync_returns_dashboard_pattern()
    {
        var patterns = await _provider.GetReferenceUiPatternsAsync(CancellationToken.None);

        patterns.Should().HaveCount(1);
        patterns[0].PatternId.Should().Be("ActiveInspectionsPage");
        patterns[0].LayoutId.Should().Be("AppLayout");
        patterns[0].Regions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetAccessibilityRulesAsync_returns_rules()
    {
        var rules = await _provider.GetAccessibilityRulesAsync(CancellationToken.None);

        rules.Should().NotBeEmpty();
        rules.Should().Contain(r => r.RuleId == "A11Y_BUTTON_FOCUS_RING");
    }

    [Fact]
    public async Task GetBestPracticesAsync_returns_naming_guide()
    {
        var practices = await _provider.GetBestPracticesAsync(new BestPracticeQuery(), CancellationToken.None);

        practices.Should().HaveCount(1);
        practices[0].Id.Should().Be("naming");
        practices[0].Title.Should().Be("Component Naming Conventions");
        practices[0].Content.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetIconsAsync_returns_icon_set()
    {
        var icons = await _provider.GetIconsAsync(new IconQuery(), CancellationToken.None);

        icons.Should().NotBeEmpty();
        icons.Should().Contain(i => i.Name == "search");
    }
}
