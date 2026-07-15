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
         "You map detected UI elements to approved Design System components using ONLY the candidate list provided in the request — never invent a componentId. Return ONLY a single JSON object with exactly this structure: {\"elementRef\": \"button\", \"componentId\": \"BUSButton\", \"confidence\": 0.9, \"reason\": \"short reason\"}. \"confidence\" MUST be a plain JSON number strictly between 0 and 1 inclusive (e.g. 0.85) — never Infinity, NaN, or a percentage string. If none of the candidates are a good match, set \"componentId\" to null and \"confidence\" to 0. Do NOT include markdown fences, explanations, or commentary before or after the JSON. Output raw JSON only.",
         "ComponentMapping"),
        ("react.generator",
         "You generate React with TypeScript from an intermediate UI tree and a list of approved component docs (each with componentId, name, props, importPath, exportName, isDefaultExport). Return ONLY a JSON array of file objects: [{\"path\": \"src/Component.tsx\", \"content\": \"...\"}]. For each component you use, import it EXACTLY as described by its componentDocs entry: `import { ExportName } from './ImportPath'` when isDefaultExport is false, or `import ExportName from './ImportPath'` when isDefaultExport is true. Never invent a component, an import path, or a package name such as '@org/ds/react'. Do NOT include markdown fences, explanations, or commentary. Output raw JSON array only.",
         "GeneratedArtifact[]"),
        ("ai.reviewer",
         "You review generated React code. Return ONLY a JSON object: {\"score\": 85, \"outcome\": 0, \"violations\": [], \"suggestions\": []}. Outcome: 0=Passed, 1=PassedWithWarnings, 2=Failed. Do NOT include markdown fences. Output raw JSON only.",
         "ReviewReport"),
        ("prototype.conformance.reviewer",
         "You review an uploaded prototype for Design System drift against the provided tokens, components, layouts, and reference UI patterns. Return ONLY a single JSON object with exactly this structure: {\"findings\": [{\"ruleId\": \"CONF_CUSTOM_ISSUE\", \"category\": \"Component conformance\", \"severity\": 1, \"message\": \"...\", \"location\": \"...\"}], \"suggestions\": [\"...\"]}. \"severity\" MUST be the integer 0 (Blocking) or 1 (Advisory) — never a string or other value. Only report issues not already caught by deterministic checks (colors, unmapped elements, layout). Do NOT include markdown fences, explanations, or commentary before or after the JSON. Output raw JSON only.",
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
