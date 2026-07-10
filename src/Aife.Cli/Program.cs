using System.Net.Http.Headers;
using Aife.Application.AI;
using Aife.Application.AI.Stages;
using Aife.Application.Knowledge;
using Aife.Application.Persistence;
using Aife.Application.Pipeline;
using Aife.Application.Prompting;
using Aife.Ai.Stages;
using Aife.Domain.Generation;
using Aife.Infrastructure.AI;
using Aife.Infrastructure.Persistence;
using Aife.Knowledge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// ── Paths ──
var repoRoot = FindRepoRoot();
var knowledgePath = Path.Combine(repoRoot, "knowledge");
var dataPath = Path.Combine(repoRoot, "data");
Directory.CreateDirectory(dataPath);

// ── DI ──
var services = new ServiceCollection();

services.AddSingleton<IKnowledgeProvider>(_ => new JsonKnowledgeProvider(knowledgePath));

// ── LLM: try reading DeepSeek config from src/Aife.Api/appsettings.Development.json ──
var configPath = Path.Combine(repoRoot, "src", "Aife.Api", "appsettings.Development.json");
var llmProviders = new List<ILlmProvider>();

if (File.Exists(configPath))
{
    var config = new ConfigurationBuilder().AddJsonFile(configPath).Build();
    var llmSection = config.GetSection("LLM:Providers");
    foreach (var child in llmSection.GetChildren())
    {
        var name = child.Key;
        var endpoint = child.GetValue<string>("Endpoint");
        var apiKey = child.GetValue<string>("ApiKey");
        var model = child.GetValue<string>("Model");
        var priority = child.GetValue("Priority", 10);

        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(model))
        {
            var httpClient = new HttpClient { BaseAddress = new Uri(endpoint.TrimEnd('/') + "/") };
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            llmProviders.Add(new OpenAiCompatibleProvider(httpClient, name, model, priority));
            Console.WriteLine($"Registered LLM: {name} ({model})");
        }
    }
}

if (llmProviders.Count == 0)
{
    llmProviders.Add(new StubLlmProvider());
    Console.WriteLine("No LLM keys found. Using StubLlmProvider.");
}

services.AddSingleton(llmProviders.AsEnumerable());
services.AddSingleton<LlmRouter>();

services.AddSingleton<IPromptRepository>(_ => new FilePromptRepository(dataPath));
services.AddSingleton<IPromptVersionRepository>(_ => new FilePromptVersionRepository(dataPath));
services.AddSingleton<IPromptManager, PromptManager>();

services.AddSingleton<IPrototypeRepository>(_ => new FilePrototypeRepository(dataPath));
services.AddSingleton<ISessionRepository>(_ => new FileSessionRepository(dataPath));
services.AddSingleton<IArtifactRepository>(_ => new FileArtifactRepository(dataPath));

services.AddSingleton<IPrototypeAnalyzer, PrototypeAnalyzer>();
services.AddSingleton<IComponentMapper, ComponentMapper>();
services.AddSingleton<IUiTreeAssembler, UiTreeAssembler>();
services.AddSingleton<IReactGenerator, ReactGenerator>();
services.AddSingleton<IAiReviewer, AiReviewer>();
services.AddSingleton<IPrototypeConformanceReviewer, PrototypeConformanceReviewer>();
services.AddSingleton<IPrototypeGenerator, PrototypeGenerator>();
services.AddSingleton<IConformanceReportRepository>(_ => new FileConformanceReportRepository(dataPath));
services.AddSingleton<RunGenerationSessionHandler>();

var sp = services.BuildServiceProvider();

// ── Seed prompts ──
await Aife.Cli.PromptSeeder.SeedAsync(
    sp.GetRequiredService<IPromptRepository>(),
    sp.GetRequiredService<IPromptVersionRepository>());

// ── Run ──
var handler = sp.GetRequiredService<RunGenerationSessionHandler>();
var generator = sp.GetRequiredService<IReactGenerator>();
var receiver = sp.GetRequiredService<IAiReviewer>();
var artifactRepo = sp.GetRequiredService<IArtifactRepository>();

var htmlPath = args.Length > 0 ? args[0] : null;
var treeMode = args.Contains("--tree");

if (treeMode)
{
    Console.WriteLine("=== Direct generation mode: BUSpek shell + Active Inspections ===`n");

    var tree = new IntermediateUiTree
    {
        Page = "ActiveInspections",
        Layout = "AppLayout",
        Children = new List<UiNode>
        {
            new() { NodeId = "btn-new", ComponentId = "BUSButton", Text = "New control", TokenBindings = new Dictionary<string, string> { ["bg"] = "color.primary" } },
            new() { NodeId = "tabs", ComponentId = "BUSTabStrip", Props = new Dictionary<string, object?> { ["tabs"] = "All,Started,Mine", ["activeIndex"] = 0 } },
            new() { NodeId = "btn-filter", ComponentId = "BUSButton", Text = "Filter", Variant = "outlineSecondary" },
            new() { NodeId = "btn-columns", ComponentId = "BUSButton", Text = "Columns", Variant = "outlineSecondary" },
            new() { NodeId = "btn-export", ComponentId = "BUSButton", Text = "Export", Variant = "outlineSecondary" },
            new() { NodeId = "grid", ComponentId = "DataGrid", Props = new Dictionary<string, object?> { ["columns"] = "Reg.no,Insp.#,Type,Make/model,Insp.date,Status", ["sortable"] = true } }
        }
    };

    var artifacts = await generator.GenerateAsync(tree, CancellationToken.None);

    Console.WriteLine($"Generated {artifacts.Count} file(s):");
    foreach (var a in artifacts)
    {
        Console.WriteLine($"`n=== {a.Path} ===`n{a.Content}`n");
    }

    return 0;
}

if (string.IsNullOrEmpty(htmlPath))
{
    // Demo run with a sample prototype
    htmlPath = null;
}

string html;
string css = "";

if (htmlPath is not null && File.Exists(htmlPath))
{
    html = await File.ReadAllTextAsync(htmlPath);
    var cssPath = Path.ChangeExtension(htmlPath, ".css");
    if (File.Exists(cssPath))
        css = await File.ReadAllTextAsync(cssPath);
}
else
{
    Console.WriteLine("No prototype file specified. Running demo with a sample prototype.");
    Console.WriteLine("Usage: aife <path-to-prototype.html>");
    Console.WriteLine();
    html = """
    <html>
      <body>
        <button>Submit</button>
        <table><tr><th>Name</th></tr></table>
      </body>
    </html>
    """;
}

var prototype = new Prototype
{
    Id = $"p-cli-{Guid.NewGuid():N}",
    Html = html,
    Css = css
};

Console.WriteLine($"Prototype: {prototype.Id}");
Console.WriteLine("Running pipeline...");

var result = await handler.HandleAsync(prototype, CancellationToken.None);

Console.WriteLine();
Console.WriteLine($"Session:  {result.Session.Id}");
Console.WriteLine($"Status:   {result.Session.Status}");
Console.WriteLine();

if (result.Analysis is not null)
{
    Console.WriteLine($"Analysis: layout={result.Analysis.Layout}, {result.Analysis.Elements.Count} element(s)");
}

if (result.Mappings is not null)
{
    foreach (var m in result.Mappings)
    {
        Console.WriteLine($"  Map: {m.ElementRef} -> {m.ComponentId ?? "(unmapped)"} (confidence={m.Confidence:F2})");
    }
}

Console.WriteLine();
if (result.Artifacts is not null)
{
    Console.WriteLine($"Artifacts: {result.Artifacts.Count} file(s)");
    foreach (var a in result.Artifacts)
    {
        Console.WriteLine($"  {a.Path} ({a.Content.Length} chars)");
    }
}

Console.WriteLine();
if (result.Review is not null)
{
    Console.WriteLine($"Review: score={result.Review.Score}, outcome={result.Review.Outcome}");
    if (result.Review.Violations.Count > 0)
    {
        Console.WriteLine($"  Violations: {result.Review.Violations.Count}");
    }
    if (result.Review.Suggestions.Count > 0)
    {
        foreach (var s in result.Review.Suggestions)
        {
            Console.WriteLine($"  Suggestion: {s}");
        }
    }
}

Console.WriteLine();
Console.WriteLine("Done.");

return 0;

// ── Helpers ──

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "knowledge")) &&
            Directory.Exists(Path.Combine(dir.FullName, "src")))
            return dir.FullName;
        dir = dir.Parent;
    }
    return Directory.GetCurrentDirectory();
}
