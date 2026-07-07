# AI Review Strategy

Status: Draft · Date: 2026-07-07 · Version: 0.1
Vision deliverable: 18 (AI Review Strategy)

## Summary

The AI Reviewer scores generated artifacts against four dimensions: compliance,
accessibility, architecture, and suggestions. It returns a `ReviewReport` with an
overall score, a list of violations with severity, and suggestions. Blocking
findings fail the session review; advisory findings are reported but do not
block. The reviewer reads rules and approved components through
`IKnowledgeProvider`, never directly.

## Review dimensions

| Dimension | Source of truth | Examples |
|---|---|---|
| Compliance | Knowledge Base components and tokens | Unapproved component used, hardcoded color, missing token binding. |
| Accessibility | `GetAccessibilityRulesAsync` | Missing label on input, no focus ring, table header scope missing. |
| Architecture | Generation rules | Inline styles, file structure violation, import from unknown path. |
| Suggestions | Best practices | Could use a layout slot, could consolidate duplicate props. |

## Rule set

Rules are data, not code. They come from the Knowledge Base and are versioned
with it. Each rule has a `ruleId`, a `category`, a `statement`, and a `severity`.

| Rule (illustrative) | Category | Severity |
|---|---|---|
| Only approved components may be used | Compliance | Blocking |
| No hardcoded color literals | Compliance | Blocking |
| Inputs must have an associated label | Accessibility | Blocking |
| Focus ring must be visible | Accessibility | Blocking |
| One component per file | Architecture | Advisory |
| Prefer layout slots over manual positioning | Suggestions | Advisory |

## Scoring model

The overall score is 0 to 100. Starting from 100:

- Each blocking violation subtracts a fixed weight (for example, 15).
- Each advisory finding subtracts a smaller weight (for example, 3).
- The score is floored at 0.

A session review is `Failed` if any blocking violation exists, regardless of
score. Otherwise it is `Passed`. The score is still reported for tracking and
trend analysis.

| Outcome | Condition |
|---|---|
| Passed | No blocking violations. |
| Passed with warnings | No blocking violations, one or more advisory findings. |
| Failed | One or more blocking violations. |

## Output schema

```json
{
  "sessionId": "s-1234",
  "score": 82,
  "outcome": "Passed with warnings",
  "violations": [
    {
      "ruleId": "ARCH_ONE_COMPONENT_PER_FILE",
      "category": "Architecture",
      "severity": "advisory",
      "message": "File Dashboard.tsx declares two components.",
      "location": { "file": "src/pages/Dashboard.tsx", "line": 24 }
    }
  ],
  "suggestions": []
}
```

## Reviewer inputs

The reviewer calls `IKnowledgeProvider` for:

- Approved components and tokens (compliance).
- Accessibility rules (accessibility).
- Best practices (suggestions).

It also receives the `IntermediateUiTree` and the `GeneratedArtifact` set from
the session.

## Prototype Conformance Review (input-side)

The AI Reviewer above governs the *output* (generated React). ADR-005 adds an
input-side **Prototype Conformance Review** that governs the *prototype* a
non-technical author uploads, before conversion. It shares the rule-as-data model
above but operates on a different input and produces a different report.

### Scope (MVP)

- Advisory and non-blocking. A failed report does not block conversion; it
  guides the author.
- Runs as a pre-conversion stage consuming the `PrototypeAnalysis` (layout,
  header, sidebar, footer, navigation, forms, cards, tables, buttons, dialogs,
  typography, spacing) and the raw HTML/CSS.
- Reuses `IKnowledgeProvider`, `LlmRouter`, and `PromptManager`; introduces no
  new abstraction.

### Input-side dimensions

| Dimension | Source of truth | Examples |
|---|---|---|
| Token conformance | Design token set | Hardcoded color not in palette, spacing off the token scale, radius not a token. |
| Component conformance | Approved components | Raw HTML element with no approved DS mapping, custom widget diverging from DS. |
| Layout conformance | Layout patterns + Reference UI patterns | Page structure diverging from approved layouts or current production UI. |
| Typography conformance | Design token set (typography) | Font sizes, weights, or families not drawn from tokens. |
| Accessibility (prototype) | Accessibility rules | Missing labels, no focus indication, table headers missing scope — flagged on the prototype, not just the output. |

### Reference UI patterns ("similar to current UI")

The Knowledge Base carries a new **Reference UI patterns** category: approved
page and layout patterns drawn from current production applications. The
conformance review compares the prototype's detected structure against these
reference patterns and reports drift, so prototypes stay similar to the existing
UI. These are data, versioned with the Knowledge Base; see
`01-schemas-contracts/03-knowledge-base-schema.md`.

### Output schema

```json
{
  "prototypeId": "p-001",
  "outcome": "Passed with warnings",
  "findings": [
    {
      "ruleId": "CONF_COLOR_OFF_TOKEN",
      "category": "Token conformance",
      "severity": "advisory",
      "message": "Background #123456 is not in the color token palette; nearest token is color.surface.muted.",
      "location": { "selector": ".hero", "property": "background-color" }
    }
  ],
  "suggestions": [
    "Replace #123456 with color.surface.muted to match the current dashboard hero."
  ]
}
```

### Post-MVP (deferred)

- Blocking gates: a failed conformance report prevents conversion.
- Auto-normalization: snap colors to nearest token, spacing to the scale.
- Guided authoring: DS-backed prototype templates so prototypes are conforming by
  construction. Recorded as a roadmap milestone in
  `04-cross-cutting/09-future-roadmap.md`.

## What is not shown

- Generation rules the reviewer checks against: see
  `02-ai-modules/04-react-generation-strategy.md`.
- Knowledge Base rule shape: see `01-schemas-contracts/03-knowledge-base-schema.md`.
- Testing of the reviewer: see `04-cross-cutting/05-testing-strategy.md`.
