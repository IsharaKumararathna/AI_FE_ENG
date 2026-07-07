# Component Catalog Schema

Status: Approved · Date: 2026-07-07 · Version: 1.0
Vision deliverable: 11 (Component Catalog Schema)

## Summary

Defines the shape of one Design System component entry as stored in the Knowledge
Base and returned by `IKnowledgeProvider.GetComponentAsync`. The schema is JSON
Schema draft 2020-12. Sample entries (`PrimaryButton`, `DataTable`) validate
against it and are used in contract tests.

## Schema

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://aife/schemas/component-catalog.schema.json",
  "title": "ComponentCatalogEntry",
  "type": "object",
  "required": ["componentId", "name", "category", "status", "props"],
  "properties": {
    "componentId": { "type": "string", "description": "Stable unique id, e.g. PrimaryButton" },
    "name": { "type": "string" },
    "category": {
      "enum": ["button", "input", "select", "table", "dialog", "layout", "navigation", "card", "typography", "feedback", "other"]
    },
    "status": { "enum": ["approved", "deprecated", "experimental"] },
    "description": { "type": "string" },
    "props": { "type": "array", "items": { "$ref": "#/$defs/prop" } },
    "variants": { "type": "array", "items": { "$ref": "#/$defs/variant" } },
    "tokensConsumed": { "type": "array", "items": { "type": "string" } },
    "examples": { "type": "array", "items": { "$ref": "#/$defs/example" } },
    "accessibility": { "$ref": "#/$defs/accessibility" },
    "mapsFromHtml": { "type": "array", "items": { "type": "string" }, "description": "HTML elements this maps from, e.g. button" }
  },
  "$defs": {
    "prop": {
      "type": "object",
      "required": ["name", "type"],
      "properties": {
        "name": { "type": "string" },
        "type": { "type": "string" },
        "required": { "type": "boolean", "default": false },
        "default": {},
        "description": { "type": "string" },
        "enum": { "type": "array" }
      }
    },
    "variant": {
      "type": "object",
      "required": ["name"],
      "properties": {
        "name": { "type": "string" },
        "description": { "type": "string" },
        "propOverrides": { "type": "object" }
      }
    },
    "example": {
      "type": "object",
      "required": ["name", "code"],
      "properties": {
        "name": { "type": "string" },
        "description": { "type": "string" },
        "code": { "type": "string" }
      }
    },
    "accessibility": {
      "type": "object",
      "properties": {
        "role": { "type": "string" },
        "keyboardSupport": { "type": "boolean" },
        "ariaProps": { "type": "array", "items": { "type": "string" } },
        "notes": { "type": "array", "items": { "type": "string" } }
      }
    }
  }
}
```

## Sample: PrimaryButton

```json
{
  "componentId": "PrimaryButton",
  "name": "Primary Button",
  "category": "button",
  "status": "approved",
  "description": "Main call-to-action button using the primary color token.",
  "props": [
    { "name": "label", "type": "string", "required": true },
    { "name": "onClick", "type": "function", "required": false },
    { "name": "disabled", "type": "boolean", "required": false, "default": false },
    { "name": "size", "type": "string", "required": false, "default": "medium", "enum": ["small", "medium", "large"] }
  ],
  "variants": [
    { "name": "compact", "propOverrides": { "size": "small" } }
  ],
  "tokensConsumed": ["color.action.primary", "spacing.button.padding", "radius.button"],
  "accessibility": {
    "role": "button",
    "keyboardSupport": true,
    "ariaProps": ["aria-disabled", "aria-label"],
    "notes": ["Focus ring must be visible."]
  },
  "mapsFromHtml": ["button", "a.button"]
}
```

## Sample: DataTable

```json
{
  "componentId": "DataTable",
  "name": "Data Table",
  "category": "table",
  "status": "approved",
  "description": "Accessible table for tabular data with sortable columns.",
  "props": [
    { "name": "columns", "type": "array", "required": true },
    { "name": "rows", "type": "array", "required": true },
    { "name": "sortable", "type": "boolean", "required": false, "default": false }
  ],
  "tokensConsumed": ["color.table.header", "spacing.table.cell", "border.table"],
  "accessibility": {
    "role": "table",
    "keyboardSupport": true,
    "ariaProps": ["aria-sort", "aria-rowcount"],
    "notes": ["Use scope on header cells."]
  },
  "mapsFromHtml": ["table"]
}
```

## Usage

- Stored under `knowledge/components/*.json`, one file per component.
- Loaded by `JsonKnowledgeProvider` into memory on first query.
- Validated by `Aife.Contracts.Tests` against this schema.

## What is not shown

- Knowledge Base directory layout: see `01-schemas-contracts/03-knowledge-base-schema.md`.
- How components are mapped from detected elements: see `02-ai-modules/03-component-mapping-strategy.md`.
