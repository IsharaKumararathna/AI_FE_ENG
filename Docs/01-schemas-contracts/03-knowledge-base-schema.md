# Knowledge Base Schema

Status: Draft · Date: 2026-07-07 · Version: 0.1
Vision deliverable: 13 (Knowledge Base Schema)

## Summary

Defines the on-disk Knowledge Base that `JsonKnowledgeProvider` reads in the MVP.
The base is a directory of typed files: components, tokens, layouts, reference UI
patterns, icons, best practices, and accessibility rules, plus a manifest that
indexes them. The
manifest schema is JSON Schema draft 2020-12. A small sample dataset ships under
`knowledge/` for contract tests and demos.

## On-disk layout

```text
knowledge/
  manifest.json              index of all entries
  components/
    PrimaryButton.json       conforms to component-catalog.schema.json
    DataTable.json
  tokens/
    tokens.json              conforms to design-token-set (below)
  layouts/
    AppLayout.json           conforms to layout-pattern (below)
  referenceUiPatterns/
    DashboardPage.json       conforms to reference-ui-pattern (below)
  icons/
    icons.json               conforms to icon-set (below)
  best-practices/
    naming.md                Markdown with YAML frontmatter
  accessibility/
    rules.json               conforms to accessibility-rule-set (below)
```

Markdown is permitted for best practices for human readability. Each Markdown
file has YAML frontmatter with `id`, `title`, and `tags` so the provider can
index it without parsing prose.

## Manifest schema

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://aife/schemas/knowledge-manifest.schema.json",
  "title": "KnowledgeManifest",
  "type": "object",
  "required": ["version", "components", "tokens", "layouts", "referenceUiPatterns", "icons", "bestPractices", "accessibilityRules"],
  "properties": {
    "version": { "type": "string" },
    "components": { "type": "array", "items": { "type": "string" } },
    "tokens": { "type": "string" },
    "layouts": { "type": "array", "items": { "type": "string" } },
    "referenceUiPatterns": { "type": "array", "items": { "type": "string" } },
    "icons": { "type": "string" },
    "bestPractices": { "type": "array", "items": { "type": "string" } },
    "accessibilityRules": { "type": "string" }
  }
}
```

## Supporting schemas

### Design token set

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://aife/schemas/design-token-set.schema.json",
  "title": "DesignTokenSet",
  "type": "object",
  "required": ["tokens"],
  "properties": {
    "tokens": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["name", "value", "category"],
        "properties": {
          "name": { "type": "string", "description": "e.g. color.action.primary" },
          "value": { "type": "string" },
          "category": { "enum": ["color", "spacing", "radius", "typography", "border", "shadow"] },
          "description": { "type": "string" }
        }
      }
    }
  }
}
```

### Layout pattern

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://aife/schemas/layout-pattern.schema.json",
  "title": "LayoutPattern",
  "type": "object",
  "required": ["layoutId", "name", "slots"],
  "properties": {
    "layoutId": { "type": "string", "description": "e.g. AppLayout" },
    "name": { "type": "string" },
    "description": { "type": "string" },
    "slots": { "type": "array", "items": { "type": "string" }, "description": "Named regions: header, sidebar, main, footer" }
  }
}
```

### Reference UI pattern

Captures approved page and layout patterns drawn from current production UI, so
the Prototype Conformance Review (ADR-005) can detect when an uploaded prototype
diverges from the existing application look and structure.

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://aife/schemas/reference-ui-pattern.schema.json",
  "title": "ReferenceUiPattern",
  "type": "object",
  "required": ["patternId", "name", "layoutId", "regions"],
  "properties": {
    "patternId": { "type": "string", "description": "e.g. DashboardPage" },
    "name": { "type": "string" },
    "description": { "type": "string", "description": "What current production UI this pattern is drawn from" },
    "layoutId": { "type": "string", "description": "Layout this pattern instantiates, e.g. AppLayout" },
    "regions": {
      "type": "array",
      "description": "Expected elements per layout slot, used to detect prototype drift",
      "items": {
        "type": "object",
        "required": ["slot", "element"],
        "properties": {
          "slot": { "type": "string", "description": "e.g. header, sidebar, main, footer" },
          "element": { "type": "string", "description": "e.g. AppBar, NavList, DataTable" }
        }
      }
    },
    "sourceApp": { "type": "string", "description": "Production app this pattern is sourced from" },
    "sourceVersion": { "type": "string" }
  }
}
```

### Icon set and accessibility rule set

Icon set is an array of `{ name, label, svgPath }`. Accessibility rule set is an
array of `{ ruleId, category, statement, severity }` where severity is
`blocking` or `advisory`.

## Sample dataset

The sample dataset under `knowledge/` includes:

- Two components: `PrimaryButton`, `DataTable` (see the component catalog schema
  document).
- A minimal token set with `color.action.primary`, `color.text.primary`,
  `spacing.button.padding`, `spacing.table.cell`, and `radius.button`.
- One layout: `AppLayout` with slots `header`, `sidebar`, `main`, `footer`.
- One reference UI pattern: `DashboardPage` instantiating `AppLayout` with
  regions `header=AppBar`, `sidebar=NavList`, `main=DataTable`, used by the
  Prototype Conformance Review to detect drift from the current dashboard UI.
- A small icon set and one accessibility rule (button focus ring visible).

This dataset is sufficient to exercise the full pipeline end to end in design and
in contract tests.

## Usage

- `JsonKnowledgeProvider` loads `manifest.json`, then loads each referenced file
  into typed structures on first use, cached for the process lifetime.
- Real Design System ingestion replaces these files without changing the provider
  or AI code; see `04-cross-cutting/08-enterprise-adoption.md`.

## What is not shown

- Component entry shape: see `01-schemas-contracts/01-component-catalog-schema.md`.
- How the provider exposes this to AI: see `00-foundation/08-mcp-architecture.md`.
