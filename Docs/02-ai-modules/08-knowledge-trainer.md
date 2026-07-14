# Knowledge Base Trainer

Status: Approved · Date: 2026-07-14 · Version: 1.0

## Summary

The Knowledge Trainer scans a source code repository for design tokens (SCSS
variables) and component definitions (CustomUI `.tsx`/`.jsx` files), then writes
the discovered knowledge into the `knowledge/` directory structure. It supports
two modes: **Update** (merge with existing KB) and **Replace** (wipe and rebuild).

## Interface

```
interface IKnowledgeTrainer {
    Task<TrainResult> TrainFromFolderAsync(string folderPath, TrainMode mode, CancellationToken ct)
    Task<TrainResult> TrainFromGitAsync(string gitUrl, string? branch, TrainMode mode, CancellationToken ct)
}

enum TrainMode {
    Update = 0,   // Merge discovered components with existing manifest
    Replace = 1,  // Wipe all existing components, write only discovered ones
}
```

## Training modes

| Mode | Components | Tokens | Manifest |
|---|---|---|---|
| **Update** | Writes new component files; does NOT delete old ones. | Overwrites `tokens.json`. | Merges new entries with existing; existing entries survive. |
| **Replace** | Deletes ALL existing `components/*.json` first, then writes only discovered ones. | Overwrites `tokens.json`. | Overwrites manifest with only discovered entries. |

## Training flow

```
POST /api/v1/knowledge/train { folderPath: "...", mode: "update" }
  → KnowledgeTrainerService.TrainFromFolderAsync()
    1. Find Variables.scss → ExtractTokens() → Write tokens/tokens.json
    2. IF mode == Replace: delete all knowledge/components/*.json
    3. FindComponentFiles() → scan CustomUI directories
    4. ExtractComponent() → for each .tsx/.jsx:
       - Extract component name, category, props from TypeScript interface
       - Infer tokensConsumed, mapsFromHtml
       - Write knowledge/components/{ComponentId}.json
    5. UpdateManifest(components, mode):
       - Replace: manifest = only new entries
       - Update: manifest = existing + new (merged, deduplicated)
```

## What the trainer extracts

| Source | Extracted as |
|---|---|
| `Variables.scss` | `DesignToken` (name, value, category, description) |
| `CustomUIs/*/Component.tsx` | `ComponentDetail` (ComponentId, Name, Category, Props, TokensConsumed, MapsFromHtml) |

## Token classification

SCSS variables are classified by name pattern:

| Pattern | Category |
|---|---|
| `$color*`, `$*color*` | `"color"` |
| `$sp-*`, `$spacing*`, `$padding*`, `$margin*` | `"spacing"` |
| `$radius*`, `$rounded*` | `"radius"` |
| `$shadow*` | `"shadow"` |
| `$font-size*`, `$font-h*`, `$font-title*`, `$font-p*` | `"typography"` |
| `$font-weight*`, `$font-family*` | `"typography"` |
| `$*width*`, `$breakpoint*` | `"layout"` |
| `$z-index*` | `"layout"` |
| `$transition*`, `$animation*` | `"animation"` |
| `$border*` | `"border"` |
| Everything else | `"other"` |

## Component classification

Components are classified by name and folder:

| Pattern | Category |
|---|---|
| Contains "Button" or in "button" folder | `"button"` |
| Contains "Grid"/"Table" or in "grid"/"table" folder | `"table"` |
| Contains "Tab"/"Nav" or in "tab"/"nav" folder | `"navigation"` |
| Contains "Input"/"Search"/"Text" or in "input" folder | `"input"` |
| Contains "Form"/"Field" or in "form" folder | `"form"` |
| Contains "Dialog"/"Modal" | `"dialog"` |
| Contains "Checkbox"/"Check"/"Switch"/"Toggle" | `"form"` |
| Contains "Label"/"Badge"/"Chip" | `"display"` |
| Contains "Layout"/"Shell" | `"layout"` |
| Contains "Dropdown"/"Select"/"Multi" | `"input"` |
| Everything else | `"other"` |

## API endpoints

| Endpoint | Method | Purpose |
|---|---|---|
| `/api/v1/knowledge/train` | `POST` | Train from folder or Git URL |
| `/api/v1/knowledge/summary` | `GET` | Get current KB summary (component count, token count, categories) |

### Request

```json
POST /api/v1/knowledge/train
{
    "folderPath": "C:\\Projects\\my-design-system",
    "mode": "update"
}
```

```json
POST /api/v1/knowledge/train
{
    "gitUrl": "https://github.com/org/design-system.git",
    "gitBranch": "main",
    "mode": "replace"
}
```

### Response

```json
{
    "success": true,
    "tokensExtracted": 110,
    "componentsExtracted": 24,
    "warnings": [],
    "knowledgeBasePath": "C:\\...\\knowledge"
}
```

## Dashboard UI

The stakeholder dashboard (ADR-007) includes a Training section with:
- **Folder Path** input
- **Git URL** input
- **Git Branch** input (optional)
- **Update (merge)** / **Replace (wipe & rebuild)** radio buttons
- **Train Knowledge Base** button
- Results display (tokens extracted, components extracted, warnings)

## Limitations

- Only scans CustomUI-type component folders (`.tsx`/`.jsx` under `src/Components/CustomUIs/`)
- Token extraction requires `$name: value;` SCSS variable format
- Component props extraction requires TypeScript interface pattern `interface IXProps { ... }`
- Git training clones to a temp directory and deletes it after processing
- Does NOT update layouts, reference UI patterns, icons, best practices, or accessibility rules — only tokens and components

## Related

- ADR-003: Knowledge provider abstraction
- ADR-007: Stakeholder dashboard
- `03-component-mapping-strategy.md`: How components are matched
- `01-schemas-contracts/03-knowledge-base-schema.md`: KB on-disk layout
- `01-schemas-contracts/04-api-design.md`: API design
