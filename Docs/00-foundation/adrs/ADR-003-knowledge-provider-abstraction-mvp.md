# ADR-003: Knowledge access through IKnowledgeProvider, JsonKnowledgeProvider in MVP

Status: Accepted · Date: 2026-07-07

## Context

The vision mandates MCP-ready architecture from day one: the MVP may store Design
System knowledge as JSON, Markdown, or YAML, but every AI interaction must go
through an abstraction layer. No AI module may read knowledge sources directly.

## Options considered

- Abstraction only. Define `IKnowledgeProvider` and ship `JsonKnowledgeProvider`
  for the MVP. Defer the MCP server to Phase 2.
- Abstraction plus a minimal MCP server exposing one or two tools in the MVP.
- Full Phase 2 MCP server as the primary knowledge access path in the MVP.

## Decision

Ship the abstraction only. `IKnowledgeProvider` lives in `Aife.Application`.
`JsonKnowledgeProvider` in `Aife.Knowledge` reads the sample dataset under
`knowledge/` and satisfies the contract. The Phase 2 `McpKnowledgeProvider` will
implement the same contract, so AI code does not change when the MCP server
arrives.

## Consequences

- AI stages and the mapper call `IKnowledgeProvider` only. This is enforced by
  project references: `Aife.Ai` does not reference `Aife.Knowledge`.
- The `IKnowledgeProvider` method set must match the Phase 2 MCP tools
  (`search_components`, `get_component`, `get_design_tokens`, and so on) so the
  future `McpKnowledgeProvider` is a direct swap.
- The MVP carries a small sample dataset; real Design System ingestion is a later
  milestone, covered in `04-cross-cutting/08-enterprise-adoption.md`.
