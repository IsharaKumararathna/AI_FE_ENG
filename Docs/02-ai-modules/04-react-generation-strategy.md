# React Generation Strategy

Status: Approved · Date: 2026-07-07 · Version: 1.0
Vision deliverable: 17 (React Generation Strategy)

## Summary

The React Generator consumes an `IntermediateUiTree` and produces React with
TypeScript. It uses only approved components referenced in the tree, binds props
to design tokens, and emits one file per component plus a page entry point. The
output is a `GeneratedArtifact` set validated before it reaches the AI Reviewer.

## Generation rules

- Output is React with TypeScript, one file per component.
- Only components referenced by `componentId` in the tree are used. No invented
  components.
- Only approved layouts from `IKnowledgeProvider.GetLayoutPatternsAsync`.
- No inline CSS. No hardcoded colors, spacing, or radii.
- Styling uses design tokens only, resolved from `tokenBindings`.
- Props match the component's prop definitions from
  `IKnowledgeProvider.GetComponentPropsAsync`. Unknown props are dropped and
  reported.
- Accessibility attributes from the component's `accessibility` entry are
  included (role, aria props).

## Output file structure

```text
generated/
  src/
    pages/
      Dashboard.tsx          page entry point for the tree's page
    components/
      DataTable.tsx          one file per used component, wrapped for the page
      PrimaryButton.tsx
    tokens.ts                design token imports from the DS
    App.tsx                  composes the layout and page
```

The generator imports approved components from the Design System package rather
than re-implementing them. Each generated file is a thin usage wrapper, not a
copy of the component source.

## Import resolution

- Approved components are imported from the Design System package path recorded
  in the Knowledge Base (for example, `@org/ds/react`).
- Tokens are imported from the DS token module.
- The generator records the package and version it targeted on each artifact so
  reviews and future migrations are traceable.

## Token binding

`tokenBindings` map a prop or slot to a token name. The generator emits the
token reference, not a literal value:

- Tree: `"tokenBindings": { "background": "color.action.primary" }`
- Generated: `background={tokens.color.action.primary}`

The token value itself comes from the DS token module at build time, so the
generated code never hardcodes a color.

## Validation before review

Before artifacts reach the AI Reviewer, the generator validates:

- Every `componentId` in the tree is approved and present in the Knowledge Base.
- No file contains inline styles or hardcoded color literals (regex check).
- Imports resolve to known package paths.

Failed validation blocks the review step and fails the session with a clear
reason.

## Handling partial trees (unmapped elements)

When a prototype contains HTML elements with no `mapsFromHtml` match in the
Knowledge Base (see the component mapping strategy), the Intermediate UI Tree
arrives at the React Generator with only the recognized components. The generator
produces code **only for what is in the tree** — it does not generate
placeholders, stubs, or warnings for the missing elements.

The AI Reviewer is the downstream catch: it receives the partial output and may
flag the structural gap, but the generator itself produces clean, compilable
React limited to the recognized components. The conformance report (available
via `GET /prototypes/{id}/conformance`) is the recommended pre-conversion check
for catching unmapped elements before starting a session.

## What is not shown

- Review of generated code: see `02-ai-modules/05-ai-review-strategy.md`.
- Intermediate UI Tree shape: see `01-schemas-contracts/02-intermediate-ui-schema.md`.
- Component prop definitions: see `01-schemas-contracts/01-component-catalog-schema.md`.
