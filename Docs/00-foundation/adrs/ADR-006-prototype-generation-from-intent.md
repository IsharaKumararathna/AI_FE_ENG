# ADR-006: Prototype generation from intent

Status: Accepted · Date: 2026-07-07

## Context

ADR-005 added reactive prototype governance: a Prototype Conformance Review that
scores an *uploaded* prototype and reports drift. That still requires a
non-technical author (Business Analyst, Product Owner, UX Designer) to author or
upload a prototype and then fix the reported drift — i.e., back-and-forth.

The platform's core audience includes Product Owners who cannot author
DS-conformant HTML/CSS by hand. The desired experience is proactive: the PO
describes intent, and the platform generates a prototype that already follows the
Design System and all relevant rules, correct at once, with no back-and-forth.

The existing React Generator proves the pattern: generate from a structured tree
using only approved components and tokens, so output is conformant by
construction. The same pattern applies to generating the prototype itself.

## Options considered

- Prototype Generator from intent (proactive). Generate a DS-conformant HTML/CSS
  prototype from a PO's intent spec, using only Knowledge Base-approved elements.
- Auto-normalize an uploaded prototype (reactive fix). Take a rough uploaded
  prototype and auto-correct drift (snap colors to tokens, swap components, fix
  spacing). Deferred to Phase 4 per ADR-005.
- Status quo (ADR-005 review only). Report drift; author fixes it.

## Decision

Add a **Prototype Generator** AI module that produces a DS-conformant prototype
from a PO's intent. Because it pulls only approved components, design tokens,
layout patterns, and reference UI patterns from `IKnowledgeProvider`, the
generated prototype is conformant **by construction** — drift is impossible, and
the Prototype Conformance Review (ADR-005) becomes a sanity check rather than a
gate.

Scope as **Phase 1.5**: ship immediately after the core conversion MVP. It reuses
existing abstractions and the file repositories from the ADR-004 amendment, so it
requires no cloud resources.

### Generation contract

- Input: `PrototypeRequest` — an intent spec (pages, regions, components,
  content, layout hints). Natural language is accepted and normalized to the
  structured spec by an LLM call before generation.
- Output: `Prototype` (HTML/CSS) conforming to the DS, plus the
  `IntermediateUiTree` the generator used (so the pipeline can skip Analyze/Map
  as a later optimization).
- Depends on: `IKnowledgeProvider` (components, tokens, layouts, reference UI
  patterns, accessibility rules), `LlmRouter`, `PromptManager`.
- New endpoint: `POST /prototypes/generate` (async, `202` + `Location`).

### Generation rules

- Only components from `IKnowledgeProvider.SearchComponentsAsync` may be emitted.
- Only tokens from the design token set; no hardcoded colors, spacing, or radii.
- Only approved layouts; page structure mirrors a reference UI pattern where one
  exists, so the prototype is "similar to current UI."
- Accessibility attributes from the component's accessibility entry are included.
- The generated prototype feeds the existing pipeline (Analyze → Map → React).

### Auto-normalize stays deferred

Auto-normalization (fixing a rough uploaded prototype in place) remains a Phase 4
capability. It is redundant for the primary PO flow once the Generator exists
(the PO never authors a rough prototype), and it is higher-risk because it must
semantically rewrite arbitrary HTML/CSS. It is revisited when importing legacy
prototypes becomes a real demand.

## Consequences

- The platform gains a proactive authoring path: intent → conformant prototype,
  complementing the reactive review (ADR-005) and the conversion pipeline.
- No new abstraction. The stage depends on `IKnowledgeProvider`, `LlmRouter`,
  and `PromptManager` only, preserving the dependency rule and the MCP-ready
  principle.
- Conformance is by construction, so the conformance review is non-blocking for
  generated prototypes (used as a sanity check / regression guard).
- One new prompt template (`prototype.generator`) and one new endpoint.
- No change to the domain or application layers beyond adding the
  `PrototypeRequest` and generated `Prototype` contracts and the stage interface.

## Related

- ADR-003: `IKnowledgeProvider` abstraction.
- ADR-004 (amended): file repositories for the MVP; Cosmos as post-MVP target.
- ADR-005: Prototype Conformance Review (reactive), which this makes proactive.
- `02-ai-modules/06-prototype-generation-strategy.md`: generation rules and
  contracts.
- `01-schemas-contracts/04-api-design.md`: `POST /prototypes/generate`.
