using Aife.Application.Persistence;
using Aife.Application.Prompting;
using Aife.Domain.Prompting;
using FluentAssertions;
using Moq;
using Xunit;

namespace Aife.Application.Tests;

public class PromptManagerTests
{
    private readonly Mock<IPromptRepository> _promptRepo = new();
    private readonly Mock<IPromptVersionRepository> _versionRepo = new();

    [Fact]
    public async Task GetPromptAsync_resolves_current_version_and_fills_variables()
    {
        var key = "prototype.analyzer";
        var version = "1.0.0";

        _promptRepo
            .Setup(r => r.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromptTemplate { Key = key, CurrentVersion = version });

        _versionRepo
            .Setup(r => r.GetAsync(key, version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromptVersion
            {
                Key = key,
                Version = version,
                SystemMessage = "Analyze the prototype with {{html}} and {{css}}.",
                Variables = new List<string> { "html", "css" },
                OutputContract = "PrototypeAnalysis"
            });

        var manager = new PromptManager(_promptRepo.Object, _versionRepo.Object);
        var result = await manager.GetPromptAsync(
            key,
            new Dictionary<string, string> { { "html", "<button>" }, { "css", ".btn{}" } },
            CancellationToken.None);

        result.Key.Should().Be(key);
        result.Version.Should().Be(version);
        result.SystemMessage.Should().Be("Analyze the prototype with <button> and .btn{}.");
        result.OutputContract.Should().Be("PrototypeAnalysis");
    }

    [Fact]
    public async Task GetPromptAsync_throws_when_template_not_found()
    {
        _promptRepo
            .Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PromptTemplate?)null);

        var manager = new PromptManager(_promptRepo.Object, _versionRepo.Object);

        var act = async () => await manager.GetPromptAsync(
            "missing.key", new Dictionary<string, string>(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*missing.key*");
    }

    [Fact]
    public async Task GetPromptAsync_throws_when_no_current_version()
    {
        var key = "no.version";
        _promptRepo
            .Setup(r => r.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromptTemplate { Key = key, CurrentVersion = null });

        var manager = new PromptManager(_promptRepo.Object, _versionRepo.Object);

        var act = async () => await manager.GetPromptAsync(
            key, new Dictionary<string, string>(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no current version*");
    }

    [Fact]
    public async Task GetPromptAsync_throws_when_version_not_found()
    {
        var key = "missing.version";
        _promptRepo
            .Setup(r => r.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromptTemplate { Key = key, CurrentVersion = "9.9.9" });

        _versionRepo
            .Setup(r => r.GetAsync(key, "9.9.9", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PromptVersion?)null);

        var manager = new PromptManager(_promptRepo.Object, _versionRepo.Object);

        var act = async () => await manager.GetPromptAsync(
            key, new Dictionary<string, string>(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*9.9.9*");
    }
}
