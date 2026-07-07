# Testing Strategy

Status: Draft · Date: 2026-07-07 · Version: 0.1
Vision deliverable: 26 (Testing Strategy)

## Summary

Testing follows a pyramid: many fast unit tests, contract tests that validate
schemas and samples, fewer integration tests against real dependencies, and a
prompt evaluation harness for AI stages. Generation output is guarded by
golden-file tests so regressions are visible. Accessibility is checked
automatically.

## Test pyramid

| Level | Project | Scope | Speed |
|---|---|---|---|
| Unit | `Aife.Domain.Tests`, `Aife.Application.Tests` | Entities, value objects, orchestration logic, assembler. | Fast, no I/O. |
| AI stage unit | `Aife.Ai.Tests` | All stages (Analyzer, Mapper, React Generator, AI Reviewer, Prototype Generator, Conformance Reviewer) with mocked `LlmRouter` and `IKnowledgeProvider`. | Fast. |
| Knowledge unit | `Aife.Knowledge.Tests` | `JsonKnowledgeProvider` query logic against sample data. | Fast. |
| Contract | `Aife.Contracts.Tests` | JSON Schema validation of all schemas (component catalog, intermediate UI, knowledge base incl. reference UI patterns, PrototypeRequest, PrototypeConformanceReport) and sample datasets. | Fast. |
| Integration | `Aife.Infrastructure.Tests`, `Aife.Api.Tests` | MVP file repositories; post-MVP Cosmos repositories with emulator. API endpoints with test server. | Slower. |
| Prompt evaluation | `Aife.PromptEval` | Runs prompts against a scored dataset; gates prompt promotion. | Slow, scheduled. |
| Golden-file | `Aife.Contracts.Tests` | Generated output compared to committed golden files. | Medium. |

## Contract tests

- All JSON Schemas (component catalog, intermediate UI, knowledge base including
  reference UI patterns, PrototypeRequest, PrototypeConformanceReport) are
  validated with a JSON Schema validator. Sample datasets must pass.
- API request and response payloads are validated against the OpenAPI-described
  shapes.

## AI stage tests

- Each stage is tested with a fake `ILlmProvider` that returns canned JSON. This
  verifies deserialization into typed contracts and stage logic without LLM cost.
- The Prototype Generator is tested for conformance-by-construction: with a fake
  provider returning only approved components, the generated prototype must pass
  validation (no hardcoded literals, only approved components/tokens/layouts).
- The Prototype Conformance Reviewer is tested with a prototype containing known
  drift (off-token color, unmapped component) and must report the expected
  advisory findings.
- A small set of tests calls a real provider in `stage` with a recorded response
  cache, run nightly.

## Golden-file tests

- A fixed `IntermediateUiTree` is run through the generator. The resulting
  `GeneratedArtifact` set is compared to a committed golden file.
- A fixed `PrototypeRequest` is run through the Prototype Generator; the
  generated `Prototype` (HTML/CSS) is compared to a committed golden file.
- A change to golden files requires a reviewer to confirm the change is intended.

## Prompt evaluation harness

- `Aife.PromptEval` runs each prompt version against a scored dataset and records
  accuracy, compliance rate, token cost, and latency.
- A version below threshold cannot become `currentVersion`. See
  `02-ai-modules/02-prompt-versioning.md`.

## Accessibility checks

- Generated code is scanned for required accessibility attributes (labels on
  inputs, focus rings, table header scope) as part of the AI Reviewer and as a
  static check in contract tests.

## What is not shown

- CI integration of these tests: see `04-cross-cutting/03-cicd-strategy.md`.
- Reviewer rule set: see `02-ai-modules/05-ai-review-strategy.md`.
