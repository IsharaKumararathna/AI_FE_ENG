# Handling Unknown and Unmapped UI Elements

Status: Approved · Date: 2026-07-10 · Version: 1.2
Updated: 2026-07-15 — Documented the deterministic MCP-driven flow (`match_element`,
`check_token_conformance`, `score_prototype`) alongside the existing advisory HTTP flow.

## Summary

When a Product Owner or Business Analyst creates a prototype with UI elements
that have **no matching entry in the Knowledge Base**, the platform has three
distinct behaviors depending on how the prototype enters the pipeline: two via
the legacy HTTP API, and one via the new deterministic MCP tools used by a
host coding agent. This document explains all three, the current limitations,
and what users should expect.

## Three entry points, three behaviors

| Flow | Entry point | Who uses it |
|---|---|---|
| **Upload flow** | `POST /api/v1/prototypes` | BA/PO uploads an existing HTML/CSS prototype |
| **Generate flow** | `POST /api/v1/prototypes/generate` | PO describes intent; the Prototype Generator builds one |
| **MCP-driven flow** | `match_element` / `check_token_conformance` / `score_prototype` MCP tools | A host coding agent (e.g. GitHub Copilot) reasons over a rough HTML/CSS prototype itself and calls these tools for deterministic KB lookups |

These flows handle unknown elements differently.

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

## MCP-driven flow: what happens with unknown elements

This flow is for a **host coding agent** (e.g. GitHub Copilot in VS Code with
the Aife MCP server registered) working directly inside a real consumer
project — see [10-mcp-vscode-extension.md](../04-cross-cutting/10-mcp-vscode-extension.md).
Unlike the two HTTP flows, the MCP server does **no LLM reasoning itself** —
the host agent reads the rough/incomplete HTML+CSS with its own model and
calls three deterministic tools for KB lookups, matching, and scoring.

### Step-by-step

1. **Host agent reads the prototype** — no upload/analyze API call; the agent
   parses the HTML/CSS directly (it already has full codebase context).

2. **For every detected element, call `match_element`** with `{ kind, text? }`:
   - **Exact `mapsFromHtml` match found** → one or more candidates at
     confidence 1.0 (boosted up to +0.1 if `text` resembles the component's
     name/description).
   - **No exact match, but a category fallback applies** → candidates at
     confidence 0.6.
   - **Nothing matches at all** → an **empty list**. This is the key
     difference from the HTTP flows: there is no null-`ComponentId` sentinel
     and no thrown exception — an empty array is a normal, expected response
     the agent must handle explicitly.

3. **Assemble the page**, keeping matches with confidence ≥ 0.5 and using
   their exact `importPath`/`exportName`/`isDefaultExport` to write real
   imports. For elements with no candidate (or confidence < 0.5), **do not
   invent a component or a plausible-looking import** — track them as
   unmatched and surface them to the user later.

4. **Write the `.jsx`/`.tsx` file directly into the project's real source
   folder** using the real imports from step 3, so the project's own dev
   server renders it live. No custom preview/bundler is involved.

5. **Call `check_token_conformance`** with the prototype's CSS to get
   hardcoded color/spacing violations plus a token sub-score.

6. **Call `score_prototype`** with every element's best `match_element`
   result (`kind`, `text`, `matchedComponentId`, `confidence`) and the
   violations from step 5. This returns one equal-weighted 0-100
   `finalScore`, the `matchScore`/`tokenScore`/`a11yScore` breakdown, the
   `unmatchedElements` list, and actionable `suggestions`.

7. **Present the user** the file location, the score breakdown, and the
   unmatched-element suggestions — so a non-technical PO immediately sees
   what needs attention instead of having to fine-tune the HTML themselves
   up front.

### Example

```
Rough prototype: <button>Save</button> <table></table> <slider>Volume</slider>

match_element("button")               -> [{ componentId: "BUSButton", confidence: 1.0, ... }]
match_element("table")                -> [{ componentId: "BUSGrid",   confidence: 1.0, ... }]
match_element("slider")               -> []   <- no approved component; tracked as unmatched

Written: src/Pages/Generated/Dashboard.jsx (imports BUSButton, BUSGrid — real paths)

check_token_conformance(css)          -> { violations: [...], score: 90 }
score_prototype({ elements, tokenViolations })
  -> { finalScore: 71.7, matchScore: 66.7, tokenScore: 90, a11yScore: 50,
       unmatchedElements: ["slider"],
       suggestions: ["No confident component match for 'slider' — ...", ...] }
```

The PO gets working, interactive code using real components immediately,
plus a clear number and list of what still needs a human decision — instead
of silently-dropped elements (upload flow) or a hard rejection (generate flow).

### Why this flow exists

Non-technical users lose significant time hand-tuning HTML/CSS to exactly
match component conventions before either HTTP flow will accept it. The
MCP-driven flow inverts this: the host agent (with its own general reasoning)
absorbs the messiness of a rough prototype, and only the **deterministic,
reproducible parts** — matching, token checks, scoring — are delegated to the
MCP server. See `Docs/04-cross-cutting/10-mcp-vscode-extension.md` for setup
and the full tool reference.

### Why the difference (HTTP flows)?

The upload flow starts from raw HTML — the platform doesn't control what the
PO authors, so it can't reject the input. It degrades gracefully.

The generate flow is a **structured request** — the PO explicitly names
components. If a named component doesn't exist, that's an error the PO can
fix, so the platform rejects immediately rather than producing a silently
wrong result.

## Current limitation

All three flows share the same fundamental limitation: **if a UI element or
component has no entry in the Knowledge Base, it cannot appear in the
generated output.**

- **Upload flow**: Silently dropped. PO gets partial output.
- **Generate flow**: Rejected with an error. PO must fix and retry.
- **MCP-driven flow**: `match_element` returns an empty list; the host agent
  must track it as unmatched and report it via `score_prototype`'s
  `unmatchedElements`/`suggestions` rather than fabricating a component.

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
| Component Matching Service (MCP) | `src/Aife.Knowledge/ComponentMatchingService.cs` | `MatchElementAsync` — returns `[]` on no match, never throws |
| Token Conformance Checker (MCP) | `src/Aife.Knowledge/TokenConformanceChecker.cs` | `CheckAsync` — regex-based color/spacing violations |
| Prototype Scorer (MCP) | `src/Aife.Knowledge/PrototypeScorer.cs` | `ScoreAsync` — equal-weighted match/token/a11y score |

## Related

- [Component Mapping Strategy](03-component-mapping-strategy.md) — mapping logic and confidence thresholds
- [Prototype Generation Strategy](06-prototype-generation-strategy.md) — generate flow details
- [React Generation Strategy](04-react-generation-strategy.md) — generator output rules
- [Domain Model](../00-foundation/04-domain-model.md) — "Behavior on novel/unmapped elements" invariants
- [Component Catalog Schema](../01-schemas-contracts/01-component-catalog-schema.md) — KB entry format
- [MCP Server — VS Code Extension Integration](../04-cross-cutting/10-mcp-vscode-extension.md) — MCP-driven flow setup and full tool reference

## v1.1 Change Log (2026-07-15)

- **Conformance Reviewer JSON parsing**: Fixed crash when LLM returns Markdown
  text before JSON. Now extracts JSON from `[{` boundaries before deserializing.
  Previously the raw response was passed directly to `JsonConvert.Deserialize`,
  which threw `JsonReaderException` on non-JSON text. Now fails gracefully
  with a logged warning and a fallback `Passed` report.
- **AiReviewer JSON parsing**: Already had fallback behavior (returns
  `PassedWithWarnings` with score 50 on parse failure), confirmed working.
- **ReactGenerator**: Added file-system output in CLI mode so generated
  `.tsx` files are written to `output/` next to the input HTML, making
  it straightforward to compare different prototype inputs.

## v1.2 Change Log (2026-07-15)

- **New MCP-driven flow documented**: added the third entry point using the
  deterministic `match_element` / `check_token_conformance` / `score_prototype`
  MCP tools, for host coding agents working directly in a consumer project.
  Unlike both HTTP flows, an unmatched element is a normal empty-list return
  (never a thrown exception or a silent drop) that the host agent must
  explicitly account for in the score/suggestions it presents to the user.
- Added the three new MCP-backing classes
  (`ComponentMatchingService`/`TokenConformanceChecker`/`PrototypeScorer`) to
  the technical reference table.
