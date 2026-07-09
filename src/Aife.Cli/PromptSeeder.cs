using Aife.Application.Persistence;
using Aife.Domain.Prompting;

namespace Aife.Cli;

internal static class PromptSeeder
{
    private static readonly (string Key, string SystemMessage, string OutputContract)[] Templates =
    {
        ("prototype.analyzer",
         "You analyze an HTML and CSS prototype and return a structured description of layout regions and UI primitives.",
         "PrototypeAnalysis"),
        ("component.mapper",
         "You map detected elements to approved Design System components. Only use components from the provided list.",
         "ComponentMapping[]"),
        ("react.generator",
         "You generate React with TypeScript from an intermediate UI tree. Use only approved components and design tokens.",
         "GeneratedArtifact[]"),
        ("ai.reviewer",
         "You review generated React code for compliance, accessibility, and architecture.",
         "ReviewReport"),
        ("prototype.conformance.reviewer",
         "You review an uploaded prototype for Design System drift against tokens, components, layouts, and reference UI patterns.",
         "PrototypeConformanceReport"),
        ("prototype.generator",
         "You generate a Design-System-conformant HTML and CSS prototype from a PrototypeRequest. Use only approved components and design tokens.",
         "GeneratedPrototype")
    };

    public static async Task SeedAsync(
        IPromptRepository promptRepo,
        IPromptVersionRepository versionRepo,
        CancellationToken ct = default)
    {
        foreach (var (key, systemMessage, outputContract) in Templates)
        {
            var existing = await promptRepo.GetAsync(key, ct);
            if (existing is not null)
                continue;

            await promptRepo.SaveAsync(new PromptTemplate { Key = key, CurrentVersion = "1.0.0" }, ct);
            await versionRepo.SaveAsync(new PromptVersion
            {
                Key = key,
                Version = "1.0.0",
                SystemMessage = systemMessage,
                Variables = new List<string>(),
                OutputContract = outputContract
            }, ct);
        }
    }
}
