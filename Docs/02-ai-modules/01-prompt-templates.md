# Prompt Templates

Status: Draft · Date: 2026-07-07 · Version: 0.1
Vision deliverable: 14 (Prompt Templates)

## Summary

Each AI stage uses a versioned prompt template assembled by the Prompt Manager.
Templates define a system message, variables, a few-shot example, and an output
contract. Output contracts reference the JSON Schemas so stage output deserializes
into typed structures. Templates are stored as versioned resources; see
`02-ai-modules/02-prompt-versioning.md`.

## Template structure

Every template has:

- `key`: stage identifier (for example, `prototype.analyzer`).
- `systemMessage`: role and constraints for the model.
- `variables`: placeholders filled by the stage at runtime.
- `outputContract`: the JSON Schema the response must satisfy, enabling JSON mode.
- `examples`: one or more few-shot input and output pairs.

## Prototype Analyzer

- Key: `prototype.analyzer`
- Variables: `{{html}}`, `{{css}}`
- System message: "You analyze an HTML and CSS prototype and return a structured
  description of layout regions and UI primitives. Use only the detected element
  kinds: header, sidebar, footer, navigation, form, card, table, button, dialog,
  typography, spacing. Return JSON matching the PrototypeAnalysis schema."
- Output contract: `PrototypeAnalysis` with a `layout` and a `DetectedElement`
  list. Each element has `kind`, `text`, and `bounds`.
- Few-shot: a small HTML snippet mapped to its `PrototypeAnalysis` JSON.

## Component Mapper

- Key: `component.mapper`
- Variables: `{{detectedElements}}`, `{{availableComponents}}` (from
  `IKnowledgeProvider.SearchComponentsAsync`)
- System message: "You map detected elements to approved Design System
  components. Only use components from the provided list. For each element return
  a component id, a confidence score between 0 and 1, and a reason. If no approved
  component fits, return componentId null and set confidence to 0. Return JSON
  matching the ComponentMapping schema."
- Output contract: array of `ComponentMapping`.
- Few-shot: a `DetectedElement` of kind `button` mapped to `PrimaryButton` with
  confidence 0.95.

## React Generator

- Key: `react.generator`
- Variables: `{{intermediateUiTree}}`, `{{componentDocs}}` (props and examples
  from `IKnowledgeProvider`), `{{tokens}}`
- System message: "You generate React with TypeScript from an intermediate UI
  tree. Use only the approved components referenced in the tree. Bind props to
  design tokens, never to hardcoded colors or inline styles. One file per
  component. Return JSON matching the GeneratedArtifact schema: a list of file
  paths and contents."
- Output contract: array of `GeneratedArtifact`.
- Few-shot: a small `IntermediateUiTree` mapped to a `DataTable.tsx` file.

## AI Reviewer

- Key: `ai.reviewer`
- Variables: `{{artifacts}}`, `{{intermediateUiTree}}`, `{{approvedComponents}}`,
  `{{tokens}}`, `{{accessibilityRules}}`
- System message: "You review generated React code for compliance, accessibility,
  and architecture. Return a score from 0 to 100, a list of violations with
  category and severity, and suggestions. Severity is blocking or advisory.
  Return JSON matching the ReviewReport schema."
- Output contract: `ReviewReport`.
- Few-shot: a file with an inline color producing a blocking compliance
  violation.

## Prototype Generator

- Key: `prototype.generator`
- Variables: `{{prototypeRequest}}`, `{{availableComponents}}` (from
  `IKnowledgeProvider.SearchComponentsAsync`), `{{tokens}}`,
  `{{layoutPatterns}}`, `{{referenceUiPatterns}}`
- System message: "You generate a Design-System-conformant HTML and CSS prototype
  from a PrototypeRequest. Use only the provided approved components and design
  tokens. Bind styles to token CSS variables, never to hardcoded literals. Mirror
  the referenced UI pattern where one is provided. Include each component's
  accessibility attributes. Return JSON matching the generated Prototype schema:
  html, css, intermediateUiTree, tokensUsed, componentsUsed."
- Output contract: generated `Prototype` (see
  `02-ai-modules/06-prototype-generation-strategy.md`, ADR-006).
- Few-shot: a `PrototypeRequest` for a Dashboard mapped to a conformant prototype
  that uses `AppBar`, `NavList`, `DataTable`, and `PrimaryButton` bound to tokens.

## Prototype Conformance Reviewer

- Key: `prototype.conformance.reviewer`
- Variables: `{{prototypeAnalysis}}`, `{{html}}`, `{{css}}`, `{{tokens}}`,
  `{{approvedComponents}}`, `{{layoutPatterns}}`, `{{referenceUiPatterns}}`,
  `{{accessibilityRules}}`
- System message: "You review an uploaded prototype for Design System drift.
  Compare detected elements and styles against the provided tokens, approved
  components, layouts, and reference UI patterns. Return findings with category,
  severity (advisory or blocking), and a message; severity is advisory in the
  MVP. Return JSON matching the PrototypeConformanceReport schema."
- Output contract: `PrototypeConformanceReport` (see
  `02-ai-modules/05-ai-review-strategy.md`, ADR-005).
- Few-shot: a hero section with a hardcoded `#123456` background producing an
  advisory token-conformance finding with the nearest token suggestion.

## Rules across templates

- Output is always JSON validated against the named schema. The router requests
  JSON mode where the provider supports it.
- Templates never embed Design System knowledge. They receive it through
  variables populated from `IKnowledgeProvider`.
- Prompts are reviewed and versioned before use; see
  `02-ai-modules/02-prompt-versioning.md`.

## What is not shown

- Versioning and rollback: see `02-ai-modules/02-prompt-versioning.md`.
- Mapping confidence and fallback: see `02-ai-modules/03-component-mapping-strategy.md`.
- Review scoring detail: see `02-ai-modules/05-ai-review-strategy.md`.
