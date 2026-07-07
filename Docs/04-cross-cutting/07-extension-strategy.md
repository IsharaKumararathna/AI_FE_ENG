# Extension Strategy

Status: Approved · Date: 2026-07-07 · Version: 1.0
Vision deliverable: 28 (Extension Strategy)

## Summary

The platform is built around extension points so new providers, knowledge
sources, input formats, output frameworks, and rules can be added without
changing core pipeline code. Each extension point is an interface in
`Aife.Application` with a single registration at the composition root.

## Extension points

| Extension point | Interface | Example extension | Effort |
|---|---|---|---|
| LLM provider | `ILlmProvider` | A new Azure-deployed model or a third-party provider. | Add client in Infrastructure; register with the router. |
| Knowledge source | `IKnowledgeProvider` | `McpKnowledgeProvider` (Phase 2), a Storybook-backed provider, a Figma-backed provider. | Implement the interface; swap registration. |
| Input format | Prototype ingestion | Figma JSON, image, wireframe. | Add an ingestor that produces a `Prototype`-equivalent analysis input. |
| Output framework | `IReactGenerator` peer | An Angular or Vue generator. | Add a generator that consumes the same `IntermediateUiTree`. |
| Design System ingestion | Knowledge Base loader | Import a real DS from Storybook or a token file. | Replace sample files under `knowledge/`; provider and AI code unchanged. |
| Review rules | Knowledge Base content | New compliance, accessibility, or architecture rules. | Add rule entries; reviewer reads them through `IKnowledgeProvider`. |

## How extensions plug in

- New providers and knowledge sources register at the API composition root. No
  AI stage code changes because stages depend on abstractions.
- New input formats produce the same `PrototypeAnalysis` shape the mapper
  consumes, so downstream stages are reused.
- New output frameworks consume the same `IntermediateUiTree`, so mapping and
  review are reused.
- New rules are data in the Knowledge Base, not code, so the reviewer picks them
  up without a release.

## Boundaries that protect extensibility

- `Aife.Ai` does not reference `Aife.Infrastructure` or `Aife.Knowledge`. This
  prevents stages from depending on a specific provider or source.
- Stage input and output contracts are stable types in `Aife.Application`.
  Changing a contract is a major version event, not an everyday edit.
- Prompts are versioned, so a new model or provider can be validated against a
  version before it is promoted.

## What is not shown

- Phase 2 MCP server: see `04-cross-cutting/01-mcp-server-design.md`.
- Roadmap timing for these extensions: see `04-cross-cutting/09-future-roadmap.md`.
