using Aife.Application.Persistence;
using Aife.Domain.Enums;
using Aife.Domain.Generation;
using Aife.Domain.Prompting;
using Aife.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Aife.Infrastructure.Tests;

public class FileRepositoryTests
{
    private static string TempBasePath() =>
        Path.Combine(Path.GetTempPath(), "aife-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FileSessionRepository_round_trips_a_session()
    {
        var basePath = TempBasePath();
        var repo = new FileSessionRepository(basePath);
        var session = new GenerationSession
        {
            Id = "s-test-001",
            PrototypeId = "p-001",
            Status = SessionStatus.Completed,
            Stages = new List<StageState>
            {
                new() { Name = "Analyze", Status = StageStatus.Completed }
            }
        };

        await repo.SaveAsync(session, CancellationToken.None);
        var loaded = await repo.GetAsync("s-test-001", CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be("s-test-001");
        loaded.PrototypeId.Should().Be("p-001");
        loaded.Status.Should().Be(SessionStatus.Completed);
        loaded.Stages.Should().HaveCount(1);
        loaded.Stages[0].Name.Should().Be("Analyze");

        Cleanup(basePath);
    }

    [Fact]
    public async Task FileArtifactRepository_round_trips_artifacts()
    {
        var basePath = TempBasePath();
        var repo = new FileArtifactRepository(basePath);
        var sessionId = "s-002";
        var artifacts = new[]
        {
            new GeneratedArtifact { Path = ".aife/react/Dashboard.tsx", Content = "export const Dashboard = () => null;" },
            new GeneratedArtifact { Path = "src/Button.tsx", Content = "export const Button = () => null;" }
        };

        await repo.SaveAsync(sessionId, artifacts, CancellationToken.None);
        var loaded = await repo.ListAsync(sessionId, CancellationToken.None);

        loaded.Should().HaveCount(2);
        loaded.Should().Contain(a => a.Path == ".aife/react/Dashboard.tsx");

        Cleanup(basePath);
    }

    [Fact]
    public async Task FilePromptRepository_round_trips_a_template()
    {
        var basePath = TempBasePath();
        var repo = new FilePromptRepository(basePath);
        var template = new PromptTemplate
        {
            Key = "prototype.analyzer",
            CurrentVersion = "1.0.0"
        };

        await repo.SaveAsync(template, CancellationToken.None);
        var loaded = await repo.GetAsync("prototype.analyzer", CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.Key.Should().Be("prototype.analyzer");
        loaded.CurrentVersion.Should().Be("1.0.0");

        Cleanup(basePath);
    }

    [Fact]
    public async Task FilePromptVersionRepository_round_trips_and_lists_versions()
    {
        var basePath = TempBasePath();
        var repo = new FilePromptVersionRepository(basePath);
        var version = new PromptVersion
        {
            Key = "react.generator",
            Version = "1.2.0",
            SystemMessage = "You generate React.",
            Variables = new List<string> { "intermediateUiTree", "tokens" },
            OutputContract = "GeneratedArtifact[]"
        };

        await repo.SaveAsync(version, CancellationToken.None);
        var loaded = await repo.GetAsync("react.generator", "1.2.0", CancellationToken.None);
        var listed = await repo.ListAsync("react.generator", CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.Version.Should().Be("1.2.0");
        loaded.SystemMessage.Should().Be("You generate React.");
        listed.Should().HaveCount(1);

        Cleanup(basePath);
    }

    [Fact]
    public async Task FileConformanceReportRepository_round_trips_a_report()
    {
        var basePath = TempBasePath();
        var repo = new FileConformanceReportRepository(basePath);
        var report = new PrototypeConformanceReport
        {
            PrototypeId = "p-conformance-001",
            Outcome = ReviewOutcome.PassedWithWarnings,
            Findings = new List<ConformanceFinding>
            {
                new()
                {
                    RuleId = "CONF_COLOR_OFF_TOKEN",
                    Category = "Token conformance",
                    Severity = Severity.Advisory,
                    Message = "Hardcoded color not in palette."
                }
            },
            Suggestions = new List<string> { "Use color.surface.muted." }
        };

        await repo.SaveAsync(report, CancellationToken.None);
        var loaded = await repo.GetAsync("p-conformance-001", CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.PrototypeId.Should().Be("p-conformance-001");
        loaded.Outcome.Should().Be(ReviewOutcome.PassedWithWarnings);
        loaded.Findings.Should().HaveCount(1);
        loaded.Findings[0].RuleId.Should().Be("CONF_COLOR_OFF_TOKEN");

        Cleanup(basePath);
    }

    private static void Cleanup(string basePath)
    {
        if (Directory.Exists(basePath))
            Directory.Delete(basePath, recursive: true);
    }
}
