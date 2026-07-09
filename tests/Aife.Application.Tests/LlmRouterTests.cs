using Aife.Application.AI;
using FluentAssertions;
using Moq;
using Xunit;

namespace Aife.Application.Tests;

public class LlmRouterTests
{
    private static Mock<ILlmProvider> CreateProvider(
        string name, int priority, params string[] capabilities)
    {
        var mock = new Mock<ILlmProvider>();
        mock.SetupGet(p => p.Info).Returns(new LlmProviderInfo
        {
            Name = name,
            Priority = priority,
            Capabilities = capabilities.ToList(),
            IsAvailable = true
        });
        return mock;
    }

    [Fact]
    public void Constructor_throws_when_no_providers_registered()
    {
        var act = () => new LlmRouter(Array.Empty<ILlmProvider>());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SelectProvider_returns_highest_priority_when_no_capabilities_required()
    {
        var providerA = CreateProvider("secondary", priority: 50);
        var providerB = CreateProvider("azure-openai", priority: 10);

        var router = new LlmRouter(new[] { providerA.Object, providerB.Object });

        var selected = router.SelectProvider(null);
        selected.Info.Name.Should().Be("azure-openai");
    }

    [Fact]
    public void SelectProvider_matches_provider_by_capability()
    {
        var providerA = CreateProvider("azure-openai", priority: 10, "text", "json");
        var providerB = CreateProvider("vision-provider", priority: 20, "text", "vision");

        var router = new LlmRouter(new[] { providerA.Object, providerB.Object });

        var context = new StageContext
        {
            StageName = "Analyze",
            RequiredCapabilities = new List<string> { "vision" }
        };

        var selected = router.SelectProvider(context);
        selected.Info.Name.Should().Be("vision-provider");
    }

    [Fact]
    public async Task CompleteAsync_routes_to_selected_provider_and_sets_provider_name()
    {
        var provider = CreateProvider("azure-openai", priority: 10, "text", "json");
        provider
            .Setup(p => p.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse { Text = "{\"layout\":\"AppLayout\"}" });

        var router = new LlmRouter(new[] { provider.Object });

        var request = new LlmRequest
        {
            SystemMessage = "You analyze a prototype.",
            UserMessage = "<html>...</html>"
        };

        var response = await router.CompleteAsync(request, CancellationToken.None);

        response.Text.Should().Be("{\"layout\":\"AppLayout\"}");
        response.ProviderName.Should().Be("azure-openai");
        provider.Verify(
            p => p.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void SelectProvider_skips_unavailable_providers()
    {
        var providerA = CreateProvider("down", priority: 5);
        providerA.SetupGet(p => p.Info).Returns(new LlmProviderInfo
        {
            Name = "down",
            Priority = 5,
            Capabilities = new List<string>(),
            IsAvailable = false
        });

        var providerB = CreateProvider("available", priority: 50);

        var router = new LlmRouter(new[] { providerA.Object, providerB.Object });

        var selected = router.SelectProvider(null);
        selected.Info.Name.Should().Be("available");
    }
}
