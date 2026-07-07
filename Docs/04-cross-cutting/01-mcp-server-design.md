# MCP Server Design and Tool Definitions

Status: Draft · Date: 2026-07-07 · Version: 0.1
Vision deliverables: 19 (MCP Server Design), 20 (MCP Tool Definitions)

## Summary

Phase 2 exposes the Knowledge Base as an MCP server. Each `IKnowledgeProvider`
method becomes an MCP tool with an input and output JSON Schema. The Phase 2
`McpKnowledgeProvider` calls these tools, so AI code is unchanged from the MVP.
This document is a design preview; the server is not built in the MVP.

## Tool definitions

Each tool maps to one `IKnowledgeProvider` method. Input and output schemas are
the same types used in `Aife.Application`.

| Tool | Input | Output | Provider method |
|---|---|---|---|
| `search_components` | `{ query: ComponentQuery }` | `ComponentSummary[]` | `SearchComponentsAsync` |
| `get_component` | `{ componentId: string }` | `ComponentDetail` | `GetComponentAsync` |
| `get_component_props` | `{ componentId: string }` | `ComponentProps` | `GetComponentPropsAsync` |
| `get_component_examples` | `{ componentId: string }` | `ComponentExample[]` | `GetComponentExamplesAsync` |
| `get_layout_patterns` | `{}` | `LayoutPattern[]` | `GetLayoutPatternsAsync` |
| `get_design_tokens` | `{}` | `DesignTokenSet` | `GetDesignTokensAsync` |
| `get_icons` | `{ query: IconQuery }` | `Icon[]` | `GetIconsAsync` |
| `search_components_by_description` | `{ description: string }` | `ComponentSummary[]` | `SearchComponentsByDescriptionAsync` |
| `get_best_practices` | `{ query: BestPracticeQuery }` | `BestPractice[]` | `GetBestPracticesAsync` |
| `get_accessibility_rules` | `{}` | `AccessibilityRule[]` | `GetAccessibilityRulesAsync` |

## Transport

- Local development: stdio transport, launched as a subprocess by the host.
- Remote and platform use: HTTP with Server-Sent Events (Streamable HTTP),
  behind the platform API gateway.

## Authentication

- Remote transport authenticates with Entra ID bearer tokens. The server
  validates the audience and the caller role.
- The server runs under a managed identity to read the Knowledge Base source
  (files in MVP, or a knowledge service later).

## Contract equivalence

`McpKnowledgeProvider` implements `IKnowledgeProvider` by forwarding each call to
the matching tool. The return types are identical to those from
`JsonKnowledgeProvider`, so the composition root switches providers by
registration. No AI or mapper code changes.

## Relationship to the MVP

The MVP ships `JsonKnowledgeProvider` against the same method set. Designing the
tools now ensures the MVP abstraction is forward-compatible: the Phase 2 server
is an alternate backend, not a redesign.

## What is not shown

- Abstraction detail: see `00-foundation/08-mcp-architecture.md`.
- Knowledge Base content: see `01-schemas-contracts/03-knowledge-base-schema.md`.
- Auth detail: see `04-cross-cutting/04-security.md`.
