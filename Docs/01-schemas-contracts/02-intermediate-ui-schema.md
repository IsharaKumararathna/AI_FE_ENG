# Intermediate UI Schema

Status: Draft · Date: 2026-07-07 · Version: 0.1
Vision deliverable: 12 (Intermediate UI Schema)

## Summary

Defines the framework-neutral tree consumed by the React Generator. The tree is
produced from `ComponentMapping` results by the application-layer assembler, or
emitted directly by the Prototype Generator alongside a generated prototype
(ADR-006). The tree references Design System components by id and binds props to
design tokens, never to hardcoded values. JSON Schema draft 2020-12. A Dashboard
sample validates against it.

## Schema

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://aife/schemas/intermediate-ui.schema.json",
  "title": "IntermediateUiTree",
  "type": "object",
  "required": ["page", "layout", "children"],
  "properties": {
    "page": { "type": "string" },
    "layout": { "type": "string", "description": "Approved layout id, e.g. AppLayout" },
    "tokens": { "type": "object", "description": "Page-level token overrides" },
    "children": { "type": "array", "items": { "$ref": "#/$defs/uiNode" } }
  },
  "$defs": {
    "uiNode": {
      "type": "object",
      "required": ["componentId"],
      "properties": {
        "nodeId": { "type": "string" },
        "componentId": { "type": "string" },
        "variant": { "type": "string" },
        "props": { "type": "object" },
        "tokenBindings": { "type": "object", "description": "Maps a prop or slot to a design token name" },
        "text": { "type": "string" },
        "children": { "type": "array", "items": { "$ref": "#/$defs/uiNode" } }
      },
      "additionalProperties": false
    }
  }
}
```

## Rules enforced downstream

- `componentId` must exist as an approved `KnowledgeEntry` (checked by the
  generator before emitting code).
- `props` values are literals or token references; no inline CSS, no hardcoded
  colors.
- `tokenBindings` keys map to real design tokens returned by
  `IKnowledgeProvider.GetDesignTokensAsync`.

## Sample: Dashboard

```json
{
  "page": "Dashboard",
  "layout": "AppLayout",
  "children": [
    {
      "nodeId": "n1",
      "componentId": "PageHeader",
      "props": { "title": "Dashboard" },
      "tokenBindings": { "titleColor": "color.text.primary" }
    },
    {
      "nodeId": "n2",
      "componentId": "DataTable",
      "variant": "default",
      "props": {
        "columns": ["Name", "Status", "Updated"],
        "rows": []
      },
      "tokenBindings": {
        "headerColor": "color.table.header",
        "cellPadding": "spacing.table.cell"
      }
    },
    {
      "nodeId": "n3",
      "componentId": "PrimaryButton",
      "props": { "label": "Refresh", "size": "medium" },
      "tokenBindings": { "background": "color.action.primary" }
    }
  ]
}
```

## Usage

- Produced by the application-layer assembler from `ComponentMapping` results.
- Also emitted by the Prototype Generator alongside a generated prototype, so the
  pipeline can skip Analyze/Map for generated prototypes (ADR-006).
- Consumed by `IReactGenerator`.
- Validated by `Aife.Contracts.Tests` against this schema.

## What is not shown

- How mappings become nodes: see `02-ai-modules/03-component-mapping-strategy.md`.
- How nodes become React files: see `02-ai-modules/04-react-generation-strategy.md`.
