# Handling Unknown and Unmapped UI Elements

Status: Approved · Date: 2026-07-10 · Version: 1.0

## Summary

When a Product Owner or Business Analyst creates a prototype with UI elements
that have **no matching entry in the Knowledge Base**, the platform has two
distinct behaviors depending on how the prototype enters the pipeline. This
document explains both flows, the current limitations, and what users should
expect.

## Two entry points, two behaviors

The platform has two ways to get a prototype:

| Flow | Entry point | Who uses it |
|---|---|---|
| **Upload flow** | `POST /api/v1/prototypes` | BA/PO uploads an existing HTML/CSS prototype |
| **Generate flow** | `POST /api/v1/prototypes/generate` | PO describes intent; the Prototype Generator builds one |

These two flows handle unknown elements differently.

## Upload flow: what happens to unknown elements

### Step-by-step

1. **Prototype Analyzer** — The LLM scans the HTML and returns a
   `PrototypeAnalysis` with a `DetectedElement` list. Every element it finds
   gets a `kind` (e.g., `button`, `slider`, `calendar`). The LLM detects
   elements regardless of whether they are in the Knowledge Base.

2. **Prototype Conformance Reviewer** — Checks each element against the
   Knowledge Base's `mapsFromHtml` fields. Elements with no match produce an
   advisory `CONF_UNMAPPED_ELEMENT` finding. This is **non-blocking** — the
   pipeline continues.

3. **Component Mapper** — For each element, calls `FindCandidates` which
   searches every component's `mapsFromHtml` field:

   - **One match found** → High-confidence mapping (0.95), baseline match
   - **Multiple matches** → LLM refinement with candidates
   - **No match at all** → `ComponentId = null, Confidence = 0.0`

4. **UI Tree Assembler** — Filters out any mapping with confidence < 0.5 or
   null `ComponentId`. Unknown elements are **silently skipped**.

5. **React Generator** — Generates code only for the elements that made it
   into the tree. Unknown elements are absent from the output.

6. **AI Reviewer** — Scores whatever was generated.

### Result

The PO receives **partial React output** — only the recognized components
appear. The conformance report lists which elements were skipped. The PO should
check the conformance report before starting a session to understand what will
be lost.

### Example

```
PO uploads:  <button>Save</button> <slider>Volume</slider> <chart>Sales</chart>

Analysis:    layout=AppLayout, elements=[button, slider, chart]
Conformance: CONF_UNMAPPED_ELEMENT (slider), CONF_UNMAPPED_ELEMENT (chart) ← advisory
Mapper:      button → BUSButton (0.95), slider → null (0.0), chart → null (0.0)
Assembler:   skips slider and chart
Generated:   only the BUSButton component
```

## Generate flow: what happens with unknown components

### Step-by-step

1. The PO submits a `PrototypeRequest` with specific `componentId` references:

```json
{
  "pages": [{
    "regions": [{ "component": "Calendar" }],
    "actions":  [{ "component": "BUSButton", "label": "Save" }]
  }]
}
```

2. **Prototype Generator** validates every `componentId` before calling the
   LLM. If any component doesn't exist in the Knowledge Base:

   > 💥 **Immediate error**: `"Component 'Calendar' is not approved or not found in the Knowledge Base."`

3. The request is rejected with HTTP 500. No prototype is generated. The PO
   must fix the component name and retry.

### Why the difference?

The upload flow starts from raw HTML — the platform doesn't control what the
PO authors, so it can't reject the input. It degrades gracefully.

The generate flow is a **structured request** — the PO explicitly names
components. If a named component doesn't exist, that's an error the PO can
fix, so the platform rejects immediately rather than producing a silently
wrong result.

## Current limitation

Both flows have the same fundamental limitation: **if a UI element or component
has no entry in the Knowledge Base, it cannot appear in the generated output.**

- **Upload flow**: Silently dropped. PO gets partial output.
- **Generate flow**: Rejected with an error. PO must fix and retry.

The platform **never invents components**. This is by design — the platform's
core principle is that output must use only approved Design System components.

## What users should do

### Before uploading a prototype

1. Run the **conformance check** first: `GET /api/v1/prototypes/{id}/conformance`
2. Review the findings for `CONF_UNMAPPED_ELEMENT` entries
3. If unknown elements are found:
   - Add them to the Knowledge Base with correct `mapsFromHtml` tags, **or**
   - Remove them from the prototype, **or**
   - Accept that they won't appear in the generated React

### Before generating from intent

1. Verify every `componentId` you plan to use exists in the Knowledge Base
2. Use `GET /debug/knowledge/components` to browse available components
3. Only reference approved component IDs in your `PrototypeRequest`

### Adding new components to the Knowledge Base

Create a JSON file in `knowledge/components/` following the
[component catalog schema](../01-schemas-contracts/01-component-catalog-schema.md).
Key fields:

- `componentId`: Unique stable identifier
- `mapsFromHtml`: HTML elements/tags/classes that map to this component
- `status`: Must be `"approved"` for the generator to use it
- `props`: Props definition matching the actual component

Then update `knowledge/manifest.json` to include the new file.

## Technical reference

| Component | File | Key method |
|---|---|---|
| Conformance Reviewer | `src/Aife.Ai/Stages/PrototypeConformanceReviewer.cs` | `CheckUnmappedElementsAsync` — produces `CONF_UNMAPPED_ELEMENT` |
| Component Mapper | `src/Aife.Ai/Stages/ComponentMapper.cs` | `FindCandidates` — searches `mapsFromHtml` |
| UI Tree Assembler | `src/Aife.Application/AI/Stages/UiTreeAssembler.cs` | Filters `m.Confidence >= 0.5 && m.ComponentId is not null` |
| Prototype Generator | `src/Aife.Ai/Stages/PrototypeGenerator.cs` | `CollectComponentDocs` — throws on unknown componentId |

## Related

- [Component Mapping Strategy](03-component-mapping-strategy.md) — mapping logic and confidence thresholds
- [Prototype Generation Strategy](06-prototype-generation-strategy.md) — generate flow details
- [React Generation Strategy](04-react-generation-strategy.md) — generator output rules
- [Domain Model](../00-foundation/04-domain-model.md) — "Behavior on novel/unmapped elements" invariants
- [Component Catalog Schema](../01-schemas-contracts/01-component-catalog-schema.md) — KB entry format
