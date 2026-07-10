# Component Mapping Strategy

Status: Approved · Date: 2026-07-07 · Version: 1.0
Vision deliverable: 16 (Component Mapping Strategy)

## Summary

The Component Mapper turns `DetectedElement` records from the Prototype Analyzer
into `ComponentMapping` records that reference approved Design System components.
The mapper queries `IKnowledgeProvider` for candidate components, scores each
mapping for confidence, and flags low-confidence or unmatched elements for human
review. The output drives the Intermediate UI Tree assembler.

## Mapping approach

The mapper combines a deterministic baseline with an LLM step:

1. Baseline map: each `DetectedElement.kind` maps to candidate components using
   the component `mapsFromHtml` field from the Knowledge Base. This is a fast,
   deterministic lookup that does not call the LLM.
2. LLM refinement: the mapper sends the element, its context, and the candidate
   components to the LLM Router to pick the best component, variant, and props,
   and to assign a confidence score.

## Baseline mapping table

| Detected kind | Candidate component(s) |
|---|---|
| button | PrimaryButton, SecondaryButton |
| table | DataTable |
| input | TextInput |
| select | Dropdown |
| dialog | Modal |
| navigation | NavBar |
| card | Card |
| header | PageHeader |

The table is illustrative. The source of truth is the `mapsFromHtml` field on
each component in the Knowledge Base, queried through `IKnowledgeProvider`.

## Confidence scoring

Each mapping carries a confidence score between 0 and 1.

| Range | Meaning | Action |
|---|---|---|
| 0.80 to 1.00 | High confidence | Auto-apply to the Intermediate UI Tree. |
| 0.50 to 0.79 | Medium confidence | Apply but record a suggestion for review. |
| 0.00 to 0.49 | Low confidence or unmatched | Flag for human review; do not auto-apply. |

## Unmatched elements

When no approved component fits:

- The mapping `componentId` is null and confidence is 0.
- The element is recorded in the session as `UnmappedElement`.
- The review report includes an advisory finding listing unmapped elements.
- The generator skips unmapped elements rather than inventing a component.

## Human-in-the-loop override

Low-confidence and unmatched elements are exposed through the session so a human
can override:

- `POST /sessions/{id}/mappings/{elementId}` accepts a chosen `componentId` and
  optional `props`, and updates the Intermediate UI Tree.
- Overrides record the reviewer identity and timestamp for traceability.

## Novel elements with no KB mapping

When a PO creates a prototype containing HTML elements that have no
`mapsFromHtml` match in the Knowledge Base (for example, a `<carousel>`,
`<timeline>`, or `<heatmap>` that no approved component claims), the platform
does **not invent a mapping** and does **not block the pipeline**:

**Upload flow** (`POST /prototypes` → `POST /sessions`):
1. The Prototype Conformance Review (ADR-005) returns an advisory
   `CONF_UNMAPPED_ELEMENT` finding per novel element, warning the PO of the gap.
   The report is advisory — the pipeline is not blocked.
2. The Component Mapper assigns `componentId = null, confidence = 0.0`.
3. The UI Tree Assembler skips the element (confidence < 0.5 or null
   componentId). The element is absent from the Intermediate UI Tree.
4. The React Generator produces code only for the mapped elements. The novel
   elements are silently absent from the generated React.
5. The AI Reviewer scores whatever was generated; unmapped elements may be noted
   in the review report.

**Result**: the PO receives partial output — the page renders only the
recognized components. The conformance report tells them which elements were
unrecognized. The pipeline does not fail.

**Generate-from-intent flow** (`POST /prototypes/generate`):
1. If the `PrototypeRequest` references a componentId that does not exist in the
   Knowledge Base, the Prototype Generator throws an `InvalidOperationException`
   **before** any LLM call. The request is rejected with a 500 error and a
   message: `"Component 'X' is not approved or not found in the Knowledge Base."`
2. The PO corrects the componentId and resubmits.

**Guidance for POs and KB maintainers**: every novel UI element type must have a
KB entry with the correct `mapsFromHtml` tag before the upload flow can handle
it, and a valid `componentId` before the generate flow can reference it. See the
enterprise adoption document for the roll-out process.

## Intermediate UI Tree assembly

Mappings are assembled into the `IntermediateUiTree` by an application-layer
assembler, not by the mapper. The assembler:

- Places mapped components into the layout slots from the PrototypeAnalysis.
- Resolves `tokenBindings` against `IKnowledgeProvider.GetDesignTokensAsync`.
- Validates each `componentId` exists and is approved before the tree is handed
  to the generator.

## What is not shown

- Intermediate UI Tree shape: see `01-schemas-contracts/02-intermediate-ui-schema.md`.
- Generation rules: see `02-ai-modules/04-react-generation-strategy.md`.
- Review of mappings: see `02-ai-modules/05-ai-review-strategy.md`.
