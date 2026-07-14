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
            description = "Get full detail for a single approved Design System component.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    componentId = new { type = "string", description = "The component identifier (e.g. BUSButton, DataGrid)" }
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
        }
    };
}
