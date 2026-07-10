using Aife.Application.Persistence;
using Aife.Domain.Prompting;

namespace Aife.Cli;

internal static class PromptSeeder
{
    private static readonly (string Key, string SystemMessage, string OutputContract)[] Templates =
    {
        ("prototype.analyzer",
         "You are a frontend analyzer. Analyze the given HTML and CSS prototype. Return ONLY a JSON object with exactly this structure: {\"layout\": \"AppLayout\", \"elements\": [{\"kind\": \"button\", \"text\": \"Submit\"}, ...]}. Use element kinds: button, table, input, select, header, sidebar, navigation, form, card, dialog, typography. Do NOT include markdown fences, explanations, or commentary. Output raw JSON only.",
         "PrototypeAnalysis"),
        ("component.mapper",
         "You map detected elements to approved Design System components. Only use components from the provided list.",
         "ComponentMapping[]"),
        ("react.generator",
         "You generate React with TypeScript from an intermediate UI tree. Return ONLY a JSON array of file objects: [{\"path\": \"src/Component.tsx\", \"content\": \"...\"}]. Each file imports from '@org/ds/react'. Do NOT include markdown fences, explanations, or commentary. Output raw JSON array only.",
         "GeneratedArtifact[]"),
        ("ai.reviewer",
         "You review generated React code. Return ONLY a JSON object: {\"score\": 85, \"outcome\": 0, \"violations\": [], \"suggestions\": []}. Outcome: 0=Passed, 1=PassedWithWarnings, 2=Failed. Do NOT include markdown fences. Output raw JSON only.",
         "ReviewReport"),
        ("prototype.conformance.reviewer",
         "You review an uploaded prototype for Design System drift against tokens, components, layouts, and reference UI patterns.",
         "PrototypeConformanceReport"),
        ("prototype.generator",
         "You generate a Design-System-conformant HTML and CSS prototype from a PrototypeRequest.",
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
