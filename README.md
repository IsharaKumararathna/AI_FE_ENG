# AI Frontend Engineering Platform (aife)

Converts HTML/CSS prototypes into production-ready React + TypeScript applications
that conform to an organization Design System by default.

**Status**: Phase 1 MVP + Phase 1.5 implemented | Design baseline: Approved v1.0

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or later 8.x)
- No cloud resources, no emulators, no API keys required for the MVP

## Quick start

```powershell
# 1. Clone
git clone <repo-url>
cd AI_FE_ENG

# 2. Build
dotnet build

# 3. Test (62 tests)
dotnet test

# 4. Run the API (opens browser at http://localhost:5000)
dotnet run --project src\Aife.Api --launch-profile "Aife.API.Development"

# 5. Run the CLI (demo mode)
dotnet run --project src\Aife.Cli
```

## VS Code Launch Configurations (F5)

| Configuration | What it does |
|---|---|
| **API — Development** | Starts the API with DeepSeek, opens browser to dashboard |
| **API — No LLM (Stub)** | Starts the API without real LLM (uses stub provider), opens browser |
| **API — Production** | Starts the API in Production mode |
| **CLI — Run Default** | Runs the CLI with the demo prototype |
| **CLI — Run Prototype** | Runs the BUSpek prototype through the full pipeline |

**Using `dotnet run` profiles:**

```powershell
# Development (DeepSeek + dashboard)
dotnet run --project src\Aife.Api --launch-profile "Aife.API.Development"

# Stub LLM (no API key needed, dashboard works)
dotnet run --project src\Aife.Api --launch-profile "Aife.API.NoLLM"
```

## What's in the box

| Path | What |
|---|---|
| `src/Aife.Domain/` | Entities, value objects, enums — one class per file |
| `src/Aife.Application/` | Stage interfaces, `LlmRouter`, `PromptManager`, `IKnowledgeProvider`, orchestrator, repository interfaces |
| `src/Aife.Ai/` | Prototype Analyzer, Component Mapper, React Generator, AI Reviewer, Prototype Conformance Reviewer, Prototype Generator |
| `src/Aife.Knowledge/` | `JsonKnowledgeProvider` — reads the on-disk Knowledge Base |
| `src/Aife.Infrastructure/` | File repositories, stub LLM provider (swap for Azure OpenAI later) |
| `src/Aife.Api/` | REST API — composition root, controllers, prompt seeder |
| `src/Aife.Cli/` | CLI — runs the full pipeline from a terminal |
| `knowledge/` | Sample dataset: 2 components, tokens, AppLayout, DashboardPage reference pattern, icons, accessibility rules |
| `schemas/` | JSON Schemas (draft 2020-12) for all contracts |
| `tests/` | 7 test projects following the test pyramid |
| `Docs/` | Full design documentation set (Approved v1.0 baseline) |

## API endpoints (base: `/api/v1`)

```
POST   /prototypes                  Upload HTML + CSS
POST   /prototypes/generate         Generate a DS-conformant prototype from intent
GET    /prototypes/{id}             Fetch a prototype
GET    /prototypes/{id}/conformance Get conformance report (ADR-005, advisory)
POST   /sessions                    Start a generation session
GET    /sessions/{id}               Get session status + stage states
GET    /sessions/{id}/artifacts     Get generated React files
GET    /sessions/{id}/review        Get review report (score, violations, suggestions)
GET    /health                      Liveness probe
```

## Run the full flow

```powershell
# Start the API (terminal 1)
dotnet run --project src\Aife.Api

# In terminal 2:

# 1. Upload a prototype
$PROTO = Invoke-RestMethod -Uri http://localhost:5000/api/v1/prototypes `
  -Method POST -ContentType "application/json" `
  -Body '{"html":"<button>Submit</button><table><tr><th>Name</th></tr></table>","css":""}'

# 2. Check conformance
Invoke-RestMethod -Uri "http://localhost:5000/api/v1/prototypes/$($PROTO.id)/conformance"

# 3. Generate React
$SESSION = Invoke-RestMethod -Uri http://localhost:5000/api/v1/sessions `
  -Method POST -ContentType "application/json" `
  -Body "{`"prototypeId`":`"$($PROTO.id)`"}"

# 4. Get artifacts
Invoke-RestMethod -Uri "http://localhost:5000/api/v1/sessions/$($SESSION.sessionId)/artifacts"

# 5. Get review
Invoke-RestMethod -Uri "http://localhost:5000/api/v1/sessions/$($SESSION.sessionId)/review"
```

## Generate a prototype from intent (PO flow)

```powershell
Invoke-RestMethod -Uri http://localhost:5000/api/v1/prototypes/generate `
  -Method POST -ContentType "application/json" `
  -Body '{
    "intent": "A customer dashboard with a table and an Add button.",
    "pages": [{
      "name": "Dashboard",
      "layout": "AppLayout",
      "regions": [{ "slot": "main", "component": "DataTable" }],
      "actions": [{ "slot": "main", "component": "PrimaryButton", "label": "Add Customer" }]
    }]
  }'
```

## Architecture

The backend follows **Clean Architecture** with compile-time boundary enforcement:

```
Aife.Domain         ← depends on nothing
Aife.Application    ← depends on Domain
Aife.Ai             ← depends on Application only (NOT Knowledge or Infrastructure)
Aife.Knowledge      ← depends on Application
Aife.Infrastructure ← depends on Application + Domain
Aife.Api / Aife.Cli ← depends on all (composition root)
```

- **AI stages never read files or call providers directly** — they go through
  `IKnowledgeProvider` and `LlmRouter` abstractions
- **JSON serialization**: Newtonsoft.Json across the entire codebase
- **Persistence**: file-based JSON for the MVP (swappable to Cosmos DB via DI)
- **LLM**: `StubLlmProvider` in the MVP returns canned responses — swap to
  `AzureOpenAiProvider` in `Program.cs` when ready

## Conventions

- One class per file
- All JSON uses camelCase (CamelCasePropertyNamesContractResolver)
- Enums serialize as strings (StringEnumConverter)
- Errors return RFC 7807 ProblemDetails with a correlation id
- Issue branches: `aife-<issue-number>`

## Key decisions (ADRs)

| ADR | Decision |
|---|---|
| ADR-001 | .NET backend, Clean Architecture |
| ADR-002 | Multi-provider LLM router from day one |
| ADR-003 | `IKnowledgeProvider` abstraction; `JsonKnowledgeProvider` in MVP |
| ADR-004 | Cosmos DB (production); file repositories (MVP, amendment) |
| ADR-005 | Prototype Conformance Review — advisory, non-blocking input-side governance |
| ADR-006 | Prototype Generator — intent → DS-conformant prototype (Phase 1.5) |
| ADR-007 | Lightweight stakeholder dashboard — static page served by API for demos |

Full design documentation: `Docs/README.md`

## Next steps after the MVP

1. Replace `StubLlmProvider` with real LLM providers (Azure OpenAI + secondary)
2. Run the prompt evaluation harness on real LLMs before promoting prompt versions
3. Import your real Design System into the Knowledge Base format (`knowledge/`)
4. Phase 2: MCP server, guided prototype authoring, Figma/image input
