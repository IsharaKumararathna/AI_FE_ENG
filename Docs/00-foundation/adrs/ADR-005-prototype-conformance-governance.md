# ADR-005: Prototype conformance governance (input-side review)

Status: Accepted · Date: 2026-07-07

## Context

ADR-004's MVP-scope amendment established that the platform governs the
*output*: the AI Reviewer scores generated React artifacts against the Design
System. The vision's actors include Business Analysts, Product Owners, and UX
Designers, who author or update the HTML/CSS prototypes that feed the pipeline.
The MVP accepted prototypes as free-form input via `POST /prototypes` with no
check that the prototype itself follows the Design System or resembles the
current production UI.

This created a governance gap: a non-technical author could upload a prototype
with off-token colors, non-standard spacing, unmapped components, or layouts
that diverge from existing applications. Drift was never surfaced at authoring
time, only after conversion — and only on the generated React, not on the
author's intent.

## Options considered

- Output-only review (status quo). Govern generated React only; leave prototype
  authoring unchecked.
- Prototype Conformance Review as an advisory pre-conversion stage (MVP-light).
  Reuse the AI Reviewer's rule-as-data model and `IKnowledgeProvider` to score
  the uploaded prototype against the Design System and a reference UI baseline,
  non-blocking.
- Guided authoring with DS-backed templates and blocking gates. Prototypes are
  conforming by construction; non-conforming prototypes cannot be converted.

## Decision

Adopt the **Prototype Conformance Review** as an advisory, non-blocking
pre-conversion stage in the MVP (MVP-light). It consumes the `PrototypeAnalysis`
already produced by the Prototype Analyzer, plus the raw HTML/CSS, and queries
`IKnowledgeProvider` for design tokens, approved components, layout patterns,
accessibility rules, best practices, and a new **Reference UI patterns**
knowledge category. It produces a `PrototypeConformanceReport` with drift
findings, severity, and suggestions.

This mirrors the precedent set by ADR-003 (abstraction-first, file-based MVP
implementation) and ADR-004's amendment (no cloud resources required for the
MVP): the stage reuses existing abstractions and persists its output through the
file repositories defined in the ADR-004 amendment.

### MVP scope

- Advisory only. A failed conformance report does **not** block conversion. The
  author sees drift before generation begins.
- Reuses `IKnowledgeProvider`, `LlmRouter`, `PromptManager`, and the
  rule-as-data model (`ruleId`, `category`, `statement`, `severity`).
- New knowledge category **Reference UI patterns** captures approved page and
  layout patterns drawn from current production UI, so "similar to current UI"
  is data-driven and versioned with the Knowledge Base.
- New endpoint `GET /prototypes/{id}/conformance` returns the report.
- Persisted via `FileConformanceReportRepository` (file repository, per the
  ADR-004 MVP-scope amendment).

### Post-MVP (deferred)

- Guided authoring: DS-backed prototype templates so prototypes are conforming
  by construction. Recorded as a roadmap milestone in
  `04-cross-cutting/09-future-roadmap.md`.
- Blocking gates and auto-normalization (snap colors to tokens, spacing to
  scale). Recorded under Phase 4 (Engineering Governance).

## Consequences

- The pipeline gains an input-side governance stage alongside the existing
  output-side AI Reviewer. Both share the rule model but operate on different
  inputs (`PrototypeAnalysis` vs. `GeneratedArtifact` set).
- No new abstraction. The stage depends on `IKnowledgeProvider`, `LlmRouter`,
  and `PromptManager` only, preserving the dependency rule and the MCP-ready
  principle.
- The Knowledge Base gains a `referenceUiPatterns` category and manifest entry.
  This is data; new reference patterns do not require code releases.
- Non-blocking in the MVP means a non-conforming prototype can still be
  converted. The report is guidance for the author, not a gate. This is
  acceptable for the MVP and is tightened in later phases.
- No change to the domain or application layers beyond adding the
  `PrototypeConformanceReport` contract and the stage interface.

## Related

- ADR-003: `IKnowledgeProvider` abstraction, file-based MVP implementation.
- ADR-004 (amended): file repositories for the MVP; Cosmos as post-MVP target.
- `02-ai-modules/05-ai-review-strategy.md`: output-side review, shared rule
  model.
- `01-schemas-contracts/03-knowledge-base-schema.md`: Reference UI patterns
  category.
- `01-schemas-contracts/04-api-design.md`: conformance endpoint.
