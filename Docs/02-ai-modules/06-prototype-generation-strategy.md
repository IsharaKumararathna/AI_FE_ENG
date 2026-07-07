# Prototype Generation Strategy

Status: Approved · Date: 2026-07-07 · Version: 1.0
Related decision: ADR-006 (Prototype generation from intent)

## Summary

The Prototype Generator produces a Design-System-conformant HTML/CSS prototype
from a Product Owner's intent. It is the proactive counterpart to the reactive
Prototype Conformance Review (ADR-005): instead of scoring an uploaded prototype
and asking the author to fix drift, it generates a prototype that has no drift,
correct at once, so non-technical authors need no back-and-forth. Because the
generator emits only Knowledge Base-approved components, tokens, layouts, and
reference UI patterns, the output is conformant **by construction**.

This is a Phase 1.5 capability. It reuses `IKnowledgeProvider`, `LlmRouter`, and
`PromptManager`; it introduces no new abstraction and requires no cloud resources
(persisted via the file repositories from the ADR-004 amendment).

## Generation rules

- Output is HTML and CSS that renders standalone in a browser, plus the
  `IntermediateUiTree` the generator used.
- Only components from `IKnowledgeProvider.SearchComponentsAsync` may be emitted.
  No invented or raw HTML elements that lack an approved DS mapping.
- Only design tokens from the token set; no hardcoded colors, spacing, radii, or
  font values. CSS references token CSS variables (for example,
  `var(--color-action-primary)`), not literals.
- Only approved layouts from `IKnowledgeProvider.GetLayoutPatternsAsync`. Where a
  reference UI pattern exists for the requested page, the generated structure
  mirrors it, so the prototype is "similar to current UI."
- Accessibility attributes from each component's `accessibility` entry are
  included (role, aria props, labels on inputs, focus indication).
- The generated prototype feeds the existing pipeline (Analyze → Map → React). It
  is not a shortcut around the React Generator.

## Input contract: PrototypeRequest

A `PrototypeRequest` describes what the PO wants. Natural language is accepted at
the API boundary and normalized to this structured shape by an LLM call before
generation.

```json
{
  "intent": "A customer dashboard with a top header, left navigation, a customer table, and an Add Customer button.",
  "pages": [
    {
      "name": "Dashboard",
      "layout": "AppLayout",
      "referencePattern": "DashboardPage",
      "regions": [
        { "slot": "header", "component": "AppBar" },
        { "slot": "sidebar", "component": "NavList" },
        { "slot": "main", "component": "DataTable", "props": { "title": "Customers" } }
      ],
      "actions": [
        { "slot": "main", "component": "PrimaryButton", "label": "Add Customer" }
      ]
    }
  ]
}
```

- `intent`: free-text description, retained for traceability and prompt context.
- `pages`: one or more page specs. Each page names a layout and optionally a
  `referencePattern` from the Knowledge Base.
- `regions` and `actions`: component references by `componentId`. Only components
  returned by `IKnowledgeProvider` are permitted; unknown ids are rejected before
  generation.

## Output contract

```json
{
  "prototypeId": "p-generated-001",
  "html": "<!doctype html>...",
  "css": ":root { --color-action-primary: ...; } ...",
  "intermediateUiTree": { "page": "Dashboard", "layout": "AppLayout", "children": [] },
  "tokensUsed": ["color.action.primary", "spacing.button.padding", "radius.button"],
  "componentsUsed": ["AppBar", "NavList", "DataTable", "PrimaryButton"]
}
```

- `html` and `css` render a standalone preview for the PO.
- `intermediateUiTree` lets the pipeline skip Analyze/Map as a later optimization;
  in the MVP the generated prototype still flows through the normal pipeline.
- `tokensUsed` and `componentsUsed` make provenance auditable and let the
  conformance review act as a sanity check.

## Token and component binding

- The generator resolves each component's prop definitions from
  `IKnowledgeProvider.GetComponentPropsAsync`. Unknown props are dropped and
  reported, matching the React Generator's rule.
- Styling binds to token names, never literals. The emitted CSS declares the DS
  token CSS variables; values come from the DS token module, so the prototype and
  the generated React share one source of truth.
- Layout slots are filled only with components the layout pattern declares as
  valid for that slot, when the layout pattern specifies slot constraints.

## Validation before handoff

Before the generated prototype reaches the pipeline (or the PO preview), the
generator validates:

- Every `componentId` is approved and present in the Knowledge Base.
- No CSS contains hardcoded color, spacing, or radius literals (regex check).
- Every page's layout is an approved layout pattern.
- Required accessibility attributes are present for each emitted component.

Failed validation fails the generation job with a clear reason; no non-conformant
prototype is returned.

## Conformance relationship

Because the output is conformant by construction, the Prototype Conformance
Review (ADR-005) is non-blocking for generated prototypes. It is retained as a
regression guard and to surface Knowledge Base gaps (for example, a requested
component with missing accessibility metadata). For uploaded prototypes, the
conformance review remains advisory as specified in ADR-005.

## What is not shown

- Prompt template for generation: see `02-ai-modules/01-prompt-templates.md`.
- Prototype request and response endpoint: see
  `01-schemas-contracts/04-api-design.md`.
- React generation from the intermediate tree: see
  `02-ai-modules/04-react-generation-strategy.md`.
- Reactive review of uploaded prototypes: see
  `02-ai-modules/05-ai-review-strategy.md`.
