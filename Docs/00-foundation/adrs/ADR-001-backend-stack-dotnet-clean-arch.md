# ADR-001: Backend stack is .NET with Clean Architecture

Status: Accepted · Date: 2026-07-07

## Context

The platform is an AI-heavy enterprise service that orchestrates a multi-stage
pipeline (analyze, map, generate, review) and must be maintainable over multiple
phases. The team works in .NET with C# and has established conventions: one class
per file, Newtonsoft.Json for serialization, Cosmos DB with server-side query
filtering. The platform generates React and TypeScript as output, but its own
implementation language is independent of that output.

## Options considered

- .NET with C# and Clean Architecture. Matches team skills and Azure enterprise
  story. MCP SDK support is newer than the Node.js SDK.
- Node.js with TypeScript. First-class MCP SDK support and shared language with
  generated output, but diverges from team expertise and existing conventions.
- Python. Strong AI ecosystem, weaker fit for enterprise web APIs and the team
  stack.

## Decision

Build the backend in .NET with C# using Clean Architecture. Use separate
projects for AI stages (`Aife.Ai`) and knowledge access (`Aife.Knowledge`) so the
boundary between AI code and knowledge sources is enforced at compile time.

## Consequences

- The team applies existing .NET conventions directly: one class per file,
  Newtonsoft.Json, Cosmos query-side filtering.
- AI code depends on `ILlmProvider` and `IKnowledgeProvider` abstractions in
  `Aife.Application`, not on implementations, which keeps the MCP transition in
  Phase 2 free of AI code changes.
- The newer .NET MCP SDK is a minor risk, mitigated by the abstraction-first
  design: the MCP server is Phase 2 and consumes the same `IKnowledgeProvider`
  contract.
