namespace Aife.Mcp;

/// <summary>
/// MCP "prompts" — reusable, parameterized instruction templates that MCP
/// hosts (e.g. VS Code's Copilot Chat) surface directly to users as
/// slash-commands (e.g. `/mcp.aife-design-system.convert-prototype-to-page`).
///
/// Non-technical users can't be expected to type the detailed multi-step
/// instructions needed to get correct output (call match_element for every
/// element, use get_component's exact importPath, never invent a component,
/// call save_ai_preview last, etc.) — baking that sequence into a prompt
/// template here means they just fill in two simple fields instead.
///
/// IMPORTANT: The prompt template is KB-agnostic. It does NOT mention any
/// specific component names (BUSButton, BUSTabStrip, etc.). All component-
/// specific rules are derived at runtime from get_component/get_component_props
/// calls. If you train a new KB with a different component set, the prompt
/// still works without modification.
/// </summary>
public static class McpPromptDefinitions
{
    public static readonly object[] Prompts = new object[]
    {
        new
        {
            name = "convert-prototype-to-page",
            description = "Convert an HTML/CSS prototype into a real React page using only approved Design " +
                "System components and tokens, then save it as a live, reviewable preview — no manual steps, " +
                "no detailed prompt writing needed.",
            arguments = new object[]
            {
                new { name = "prototypePath", description = "Path to the prototype HTML file (e.g. docs/prototype/buspek/2/index.html). A sibling .css file with the same name, if present, is used too.", required = true },
                new { name = "slug", description = "Lowercase kebab-case name for the preview URL, e.g. 'settings'. Defaults to a name derived from the prototype's folder if omitted.", required = false }
            }
        }
    };

    /// <summary>
    /// Renders the "convert-prototype-to-page" prompt with KB-agnostic
    /// instructions. All component-specific rules come from calling
    /// get_component/get_component_props — never hardcoded.
    /// </summary>
    public static string RenderConvertPrototypeToPage(string prototypePath, string? slug)
    {
        var slugHint = string.IsNullOrWhiteSpace(slug)
            ? "a lowercase kebab-case slug you derive from the prototype's file/folder name (e.g. 'settings')"
            : $"\"{slug}\"";

        const string template = """
        Using the aife-design-system MCP tools, convert the prototype at
        {{PROTOTYPE_PATH}} (and its sibling .css file, if any) into a production
        React component. Follow these steps in order and do not skip any:

        1. Read the HTML and CSS fully.
        2. For every distinct UI element you find (buttons, tabs, toggles, radio
           groups, info boxes, tables, inputs, etc.), call `match_element` to get
           the best real Design System component match. Do not invent a
           component — if nothing matches well, say so explicitly instead of
           guessing.
        3. For each matched component, call `get_component` to get its real
           importPath/exportName/isDefaultExport, and `get_component_props` for
           its real prop API. Use these exact values in the generated code —
           never fabricate an import path or prop name.

        ⚠️ CRITICAL — General rules that apply regardless of which component
           library is in use:

        a) Import path resolution:
           - The generated .tsx file lives at _AiPreview/<slug>/ComponentName.tsx,
             which is typically 3-4 levels deep from the project's src/ root.
           - get_component returns an importPath (e.g. "Components/CustomUIs/X/X").
             The correct relative import from _AiPreview/<slug>/ is to go UP to
             the src/ root, then INTO the importPath. For a typical React project
             this means "../../../" + importPath.
           - NEVER use "../" alone — that only goes up one level and WILL break.

        b) Component props:
           - Use ONLY the props listed by get_component_props. Do NOT invent props
             (no "variant", "size", "type" unless the KB says so).
           - If a component's KB entry has "passthroughProps": true, it supports
             additional standard HTML/React props via {...rest} spread.
           - Check whether "className" is in the props list before passing it.
             If not listed, do NOT pass className — it will cause a TS error.

        c) CSS modules:
           - Every .tsx that uses className={styles.xxx} MUST include:
             `import styles from './ComponentName.module.scss';`
             as the last import statement.

        d) TypeScript callbacks:
           - NEVER write untyped callbacks like `(e) =>`. Always annotate:
             `(e: React.ChangeEvent<HTMLInputElement>) =>` for inputs/switches
             `(e: React.MouseEvent) =>` for buttons
             `(e: React.ChangeEvent<HTMLTextAreaElement>) =>` for textareas

        e) Import style:
           - If isDefaultExport is true:  `import ComponentName from './path'`
           - If isDefaultExport is false: `import { ComponentName } from './path'`
           - Use the exact exportName from get_component. Never rename on import.

        4. Call `check_token_conformance` on the CSS to get real design-token
           violations, then call `score_prototype` with your matched elements +
           those violations to get an overall conformance score.
        5. Generate the final React/TypeScript component (.tsx) and its CSS
           module (.module.scss) using ONLY the real components and exact
           import paths from step 3, and real design tokens (no invented
           custom-property names or fictional packages).
        6. Call `save_ai_preview` with:
           - slug: {{SLUG_HINT}}
           - files: the generated .tsx + .module.scss (include an index.tsx
             that does `export { default } from './ComponentName';`)
           - analysis: { layout, elements: [...] } from step 2's detected
             elements
           - review: { score, outcome, violations, suggestions } derived
             from step 4's conformance score and any issues found

        Then tell me the previewUrl it returns.
        """;

        return template
            .Replace("{{PROTOTYPE_PATH}}", prototypePath)
            .Replace("{{SLUG_HINT}}", slugHint);
    }
}
