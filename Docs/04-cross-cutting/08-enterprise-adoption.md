# Enterprise Adoption Strategy

Status: Approved · Date: 2026-07-07 · Version: 1.0
Vision deliverable: 29 (Enterprise Adoption Strategy)

## Summary

Adoption moves the platform from a sample dataset to the organization's real
Design System, then to pilot teams, then to broad use. Each step has a gate.
Governance, training, and metrics run alongside so the platform stays trustworthy
as it scales.

## Onboarding a real Design System

- Audit the existing Design System: components, tokens, layouts, icons,
  accessibility rules, best practices.
- Map each item to a Knowledge Base entry using the schemas in
  `01-schemas-contracts/`. Components conform to the component catalog schema;
  tokens to the design token set; rules to the accessibility rule set.
- Validate the imported Knowledge Base against the schemas before connecting it.
- Replace the sample dataset under `knowledge/` (or point the provider at the new
  source). AI and provider code do not change.

## Governance

- A Design System owner approves changes to the Knowledge Base. Components have
  `status` of approved, deprecated, or experimental; only approved components are
  used by the generator.
- Prompt versions require evaluation scores before promotion. Major prompt
  changes require a review.
- The AI Reviewer's blocking findings are policy, not suggestions. Teams cannot
  ship generated code that fails blocking rules without an explicit override.

## Rollout phases

| Phase | Scope | Gate to next |
|---|---|---|
| 1. Sample DS | Internal demos and contract tests. | Pipeline produces review-passing output on samples. |
| 2. Real DS import | One team's Design System loaded. | Schema validation passes; golden-file tests green. |
| 3. Pilot team | One team generates real screens. | Review pass rate above target; positive feedback. |
| 4. Broad adoption | Multiple teams and Design Systems. | Governance and metrics in place. |

## Training

- A short guide for each actor: BA uploading prototypes, PO reviewing reports,
  engineer integrating output.
- The CLI lets teams try the pipeline locally before integrating the API.

## Metrics

| Metric | Purpose |
|---|---|
| Review pass rate | Tracks output quality over time. |
| Mapping confidence distribution | Shows where the DS gaps cause low confidence. |
| Token usage and cost per session | Controls spend. |
| Time from upload to review report | Tracks responsiveness. |
| Unmapped element rate | Identifies DS coverage gaps. |

## What is not shown

- Extension points used during onboarding: see `04-cross-cutting/07-extension-strategy.md`.
- Roadmap: see `04-cross-cutting/09-future-roadmap.md`.
