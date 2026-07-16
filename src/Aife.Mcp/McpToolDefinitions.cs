namespace Aife.Mcp;

/// <summary>
/// MCP tool definitions matching the 10 IKnowledgeProvider methods
/// plus get_reference_ui_patterns (ADR-005).
/// </summary>
public static class McpToolDefinitions
{
    public static readonly object[] Tools = new object[]
    {
        new
        {
            name = "search_components",
            description = "Search the Design System component catalog by category, status, or text.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    category = new { type = "string", description = "Component category (button, table, navigation, input, form, display, layout, dialog, other)" },
                    status = new { type = "string", description = "Component status (approved, deprecated, proposed)" },
                    text = new { type = "string", description = "Free-text search in component name and description" }
                }
            }
        },
        new
        {
            name = "get_component",
            description = "Get full detail for a single approved Design System component, including its real " +
                "importPath, exportName, and isDefaultExport so you can write a correct import statement " +
                "(e.g. importPath='Components/CustomUIs/BUSButtons/BUSButton', exportName='BUSButton', " +
                "isDefaultExport=false -> `import { BUSButton } from '../Components/CustomUIs/BUSButtons/BUSButton';`). " +
                "Never invent a component's import path or package name — always use these exact values.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    componentId = new { type = "string", description = "The component identifier (e.g. BUSButton, BUSGrid)" }
                },
                required = new[] { "componentId" }
            }
        },
        new
        {
            name = "get_component_props",
            description = "Get the props/API of an approved Design System component.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    componentId = new { type = "string", description = "The component identifier" }
                },
                required = new[] { "componentId" }
            }
        },
        new
        {
            name = "get_component_examples",
            description = "Get usage examples for an approved Design System component.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    componentId = new { type = "string", description = "The component identifier" }
                },
                required = new[] { "componentId" }
            }
        },
        new
        {
            name = "get_layout_patterns",
            description = "Get all approved layout patterns (AppLayout, etc.).",
            inputSchema = new { type = "object", properties = new { } }
        },
        new
        {
            name = "get_design_tokens",
            description = "Get all design tokens (colors, spacing, radii, shadows, typography).",
            inputSchema = new { type = "object", properties = new { } }
        },
        new
        {
            name = "get_icons",
            description = "Search available icons in the Design System.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    name = new { type = "string", description = "Icon name to search for" },
                    category = new { type = "string", description = "Icon category" }
                }
            }
        },
        new
        {
            name = "search_components_by_description",
            description = "Find components by describing what you need in natural language.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    description = new { type = "string", description = "Natural language description of the component you need" }
                },
                required = new[] { "description" }
            }
        },
        new
        {
            name = "get_best_practices",
            description = "Get Design System best practices and guidelines.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    category = new { type = "string", description = "Best practice category filter" }
                }
            }
        },
        new
        {
            name = "get_accessibility_rules",
            description = "Get accessibility rules that components must satisfy.",
            inputSchema = new { type = "object", properties = new { } }
        },
        new
        {
            name = "get_reference_ui_patterns",
            description = "Get reference UI patterns drawn from current production pages (e.g. Active Inspections page).",
            inputSchema = new { type = "object", properties = new { } }
        },
        new
        {
            name = "match_element",
            description = "Deterministically match a detected HTML element (e.g. from a rough/incomplete " +
                "prototype) to the best approved Design System component(s). No LLM call is made — this is " +
                "pure rule-based matching against the Knowledge Base, so results are fast and reproducible. " +
                "Returns candidates ranked by confidence (1.0 = exact HTML-tag match declared by the " +
                "component, 0.6 = category-based fallback match, boosted slightly if 'text' resembles the " +
                "component name/description). Each candidate includes importPath/exportName/isDefaultExport " +
                "so you can write the real import directly — never invent a component or its import path. " +
                "Example: match_element({ kind: 'table', text: 'Customers' }) for a rough <table> element " +
                "-> top candidate might be { componentId: 'BUSGrid', confidence: 1.0, importPath: " +
                "'Components/CustomUIs/bus-grids/bus-grid/bus-grid', exportName: 'BUSGrid' }. " +
                "If the returned list is empty, no approved component covers this element kind — surface " +
                "that to the user rather than guessing or fabricating a component.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    kind = new { type = "string", description = "The detected HTML element kind/tag, e.g. 'button', 'table', 'input', 'tabs', 'dialog'." },
                    text = new { type = "string", description = "Optional visible text/label/placeholder near the element, used to boost confidence via simple name/description similarity." }
                },
                required = new[] { "kind" }
            }
        },
        new
        {
            name = "check_token_conformance",
            description = "Deterministically scan raw CSS for hardcoded colors/spacing values that bypass " +
                "the design token palette (hex colors, rgb()/rgba() literals, hardcoded margin/padding/gap " +
                "px values). No LLM call is made. Returns a list of violations (each with the offending " +
                "value, a message, and — when available — the nearest matching token name to suggest as a " +
                "replacement) plus a 0-100 token conformance sub-score (100 minus 10 per violation, floored " +
                "at 0). Call this once per prototype's CSS before calling score_prototype, and pass the " +
                "returned violations array straight through as 'tokenViolations'.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    css = new { type = "string", description = "The raw CSS to check, e.g. the prototype's <style> contents." }
                },
                required = new[] { "css" }
            }
        },
        new
        {
            name = "score_prototype",
            description = "Combine element-match coverage, token conformance, and accessibility coverage " +
                "into one 0-100 conformance score using equal weighting: finalScore = average(matchScore, " +
                "tokenScore, a11yScore). matchScore = % of 'elements' with a matchedComponentId and " +
                "confidence >= 0.5 (use the results you already got from match_element calls). tokenScore " +
                "comes directly from check_token_conformance's violations (pass them through as " +
                "'tokenViolations'). a11yScore = % of matched components whose Knowledge Base entry declares " +
                "accessibility metadata (role/keyboardSupport/ariaProps). Returns unmatchedElements and " +
                "actionable suggestions so you can present the user a clear breakdown of what didn't match " +
                "and why, instead of just a bare number. Call this last, after mapping every detected " +
                "element with match_element and checking the CSS with check_token_conformance.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    elements = new
                    {
                        type = "array",
                        description = "Every detected element with its best match_element result.",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                kind = new { type = "string" },
                                text = new { type = "string" },
                                matchedComponentId = new { type = "string", description = "componentId of the top match_element candidate, or omit/null if none matched." },
                                confidence = new { type = "number", description = "Confidence of the top match_element candidate (0 if none matched)." }
                            },
                            required = new[] { "kind" }
                        }
                    },
                    tokenViolations = new
                    {
                        type = "array",
                        description = "The violations array returned by check_token_conformance (pass through as-is).",
                        items = new { type = "object" }
                    }
                },
                required = new[] { "elements" }
            }
        },
        new
        {
            name = "save_ai_preview",
            description = "Saves generated page file(s) into the consumer project's live AI-preview folder " +
                "so a non-technical reviewer can see them rendered through the project's REAL build pipeline " +
                "(real webpack/sass, real design tokens) with zero manual file copying or route wiring — the " +
                "project's AiPreviewPage.tsx auto-discovers anything saved here via require.context. Call " +
                "this as the LAST step after generating a page's component (and its .module.scss if any). " +
                "'slug' must be lowercase kebab-case (e.g. 'customer-register') and becomes the URL " +
                "/ai-preview/{slug}. 'files' must include an 'index.tsx' that default-exports the page " +
                "component (e.g. `export { default } from './MyPage';`) — if omitted and exactly one .tsx " +
                "file is given, an index.tsx is auto-generated for you. Also pass 'analysis' (the detected " +
                "layout/elements from prototype analysis) and 'review' (score/outcome/violations/suggestions " +
                "from the AI reviewer) when available — the preview page shows these as 'Analysis' and " +
                "'Review Report' tabs alongside the live render and raw React code, mirroring the Aife.Api " +
                "dashboard's presentation. Both are optional; omit either if that stage wasn't run. Requires " +
                "the server to have been started with --preview-root or AIFE_PREVIEW_ROOT (or a " +
                "--components-source fallback) configured; if not, this tool returns an error explaining how " +
                "to configure it — never silently guesses a location.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    slug = new { type = "string", description = "Lowercase kebab-case identifier for the page, e.g. 'customer-register'. Becomes the URL /ai-preview/{slug}." },
                    files = new
                    {
                        type = "array",
                        description = "Files to write under the slug's preview folder. Include an index.tsx re-exporting the component's default export.",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                path = new { type = "string", description = "Relative file name within the slug folder, e.g. 'index.tsx', 'CustomerRegister.tsx', 'CustomerRegister.module.scss'. No '..' or absolute paths." },
                                content = new { type = "string", description = "Full file content." }
                            },
                            required = new[] { "path", "content" }
                        }
                    },
                    analysis = new
                    {
                        type = "object",
                        description = "Optional: the PrototypeAnalysis result (layout + detected elements) to show in the preview's 'Analysis' tab.",
                        properties = new
                        {
                            layout = new { type = "string" },
                            elements = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        kind = new { type = "string" },
                                        text = new { type = "string" },
                                        bounds = new { type = "string" }
                                    },
                                    required = new[] { "kind" }
                                }
                            }
                        }
                    },
                    review = new
                    {
                        type = "object",
                        description = "Optional: the ReviewReport result (score/outcome/violations/suggestions) to show in the preview's 'Review Report' tab.",
                        properties = new
                        {
                            score = new { type = "number" },
                            outcome = new { type = "string", description = "e.g. 'Passed', 'PassedWithWarnings', 'Failed'." },
                            violations = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        ruleId = new { type = "string" },
                                        category = new { type = "string" },
                                        severity = new { type = "string" },
                                        message = new { type = "string" },
                                        location = new { type = "string" }
                                    },
                                    required = new[] { "ruleId", "message" }
                                }
                            },
                            suggestions = new { type = "array", items = new { type = "string" } }
                        }
                    }
                },
                required = new[] { "slug", "files" }
            }
        }
    };
}
