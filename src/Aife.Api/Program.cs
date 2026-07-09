using Aife.Application.AI;
using Aife.Application.AI.Stages;
using Aife.Application.Knowledge;
using Aife.Application.Persistence;
using Aife.Application.Pipeline;
using Aife.Application.Prompting;
using Aife.Ai.Stages;
using Aife.Api.Services;
using Aife.Infrastructure.AI;
using Aife.Infrastructure.Persistence;
using Aife.Knowledge;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── Paths ──
var knowledgePath = builder.Configuration.GetValue<string>("KnowledgePath")
    ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "knowledge"));

var dataPath = builder.Configuration.GetValue<string>("DataPath")
    ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data"));

Directory.CreateDirectory(dataPath);

// ── Serilog ──
builder.Host.UseSerilog((ctx, lc) => lc
    .WriteTo.Console()
    .Enrich.WithProperty("Application", "Aife.Api"));

// ── MVC + serialization ──
builder.Services.AddControllers()
    .AddNewtonsoftJson(opts =>
    {
        opts.SerializerSettings.ContractResolver =
            new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver();
        opts.SerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;
        opts.SerializerSettings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
    });

builder.Services.AddProblemDetails();

// ── Knowledge ──
builder.Services.AddSingleton<IKnowledgeProvider>(_ => new JsonKnowledgeProvider(knowledgePath));

// ── LLM ──
builder.Services.AddSingleton<ILlmProvider, StubLlmProvider>();
builder.Services.AddSingleton<LlmRouter>();

// ── Prompts ──
builder.Services.AddSingleton<IPromptRepository>(_ => new FilePromptRepository(dataPath));
builder.Services.AddSingleton<IPromptVersionRepository>(_ => new FilePromptVersionRepository(dataPath));
builder.Services.AddSingleton<IPromptManager, PromptManager>();

// ── Repositories ──
builder.Services.AddSingleton<IPrototypeRepository>(_ => new FilePrototypeRepository(dataPath));
builder.Services.AddSingleton<ISessionRepository>(_ => new FileSessionRepository(dataPath));
builder.Services.AddSingleton<IArtifactRepository>(_ => new FileArtifactRepository(dataPath));

// ── Pipeline stages ──
builder.Services.AddSingleton<IPrototypeAnalyzer, PrototypeAnalyzer>();
builder.Services.AddSingleton<IComponentMapper, ComponentMapper>();
builder.Services.AddSingleton<IUiTreeAssembler, UiTreeAssembler>();
builder.Services.AddSingleton<IReactGenerator, ReactGenerator>();
builder.Services.AddSingleton<IAiReviewer, AiReviewer>();
builder.Services.AddSingleton<IPrototypeConformanceReviewer, PrototypeConformanceReviewer>();
builder.Services.AddSingleton<IPrototypeGenerator, PrototypeGenerator>();
builder.Services.AddSingleton<IConformanceReportRepository>(_ => new FileConformanceReportRepository(dataPath));
builder.Services.AddSingleton<RunGenerationSessionHandler>();

// ── API services ──
builder.Services.AddSingleton<ISessionResultStore, InMemorySessionResultStore>();

var app = builder.Build();

// ── Seed prompt templates on startup ──
await PromptSeeder.SeedAsync(
    app.Services.GetRequiredService<IPromptRepository>(),
    app.Services.GetRequiredService<IPromptVersionRepository>());

// ── Middleware ──
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.MapControllers();

app.Run();

// Required for WebApplicationFactory<Program> in integration tests.
public partial class Program { }
