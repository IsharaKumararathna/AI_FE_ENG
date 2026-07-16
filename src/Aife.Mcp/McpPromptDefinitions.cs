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
    /// Renders the "convert-prototype-to-page" prompt's full instruction text
    /// with the caller's arguments substituted in. Kept in one place so the
    /// exact recommended tool-call sequence (match_element -> get_component ->
    /// get_component_props -> check_token_conformance -> score_prototype ->
    /// save_ai_preview) only needs to be written and maintained once.
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
