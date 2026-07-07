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

## What is not shown

- Generation rules the reviewer checks against: see
  `02-ai-modules/04-react-generation-strategy.md`.
- Knowledge Base rule shape: see `01-schemas-contracts/03-knowledge-base-schema.md`.
- Testing of the reviewer: see `04-cross-cutting/05-testing-strategy.md`.
