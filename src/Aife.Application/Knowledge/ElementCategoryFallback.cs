namespace Aife.Application.Knowledge;

/// <summary>
/// Canonical HTML-kind → component-category fallback table. This is the
/// single source of truth for "what KB component category should cover this
/// detected HTML element kind" — used by the legacy HTTP pipeline
/// (<c>ComponentMapper</c>, <c>PrototypeConformanceReviewer</c>,
/// <c>UiTreeAssembler</c>) and, via <c>Aife.Knowledge.ComponentMatchingService</c>,
/// by the MCP <c>match_element</c> tool. Lives in Aife.Application (the
/// lowest layer referenced by everything else) specifically so it can be
/// shared without introducing a circular project reference.
/// </summary>
public static class ElementCategoryFallback
{
    public static bool MatchesSemantically(string elementKind, string category)
    {
        return Categories.TryGetValue(elementKind, out var expectedCategory)
            && string.Equals(category, expectedCategory, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Dictionary<string, string> Categories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Buttons
            ["button"] = "button",
            // Tables & grids
            ["table"] = "table",
            ["datagrid"] = "table",
            ["grid"] = "table",
            // Inputs
            ["input"] = "input",
            ["search"] = "input",
            ["search input"] = "input",
            ["textbox"] = "input",
            ["textarea"] = "input",
            ["select"] = "input",
            ["dropdown"] = "input",
            // Navigation / tabs
            ["tabs"] = "navigation",
            ["tab"] = "navigation",
            ["tabstrip"] = "navigation",
            ["sidebar"] = "navigation",
            ["navigation"] = "navigation",
            ["nav"] = "navigation",
            ["menu"] = "navigation",
            // Forms
            ["form"] = "form",
            ["formfield"] = "form",
            ["checkbox"] = "form",
            ["switch"] = "form",
            ["toggle"] = "form",
            ["radio"] = "form",
            // Layout / structure
            ["header"] = "layout",
            ["footer"] = "layout",
            ["card"] = "layout",
            ["dialog"] = "layout",
            ["modal"] = "layout",
            ["layout"] = "layout",
            // Chips / badges
            ["chips"] = "display",
            ["chip"] = "display",
            ["badge"] = "display",
            ["tag"] = "display",
            ["label"] = "display",
            // Typography
            ["typography"] = "display",
            ["text"] = "display",
            ["heading"] = "display",
            ["title"] = "display",
            // Misc
            ["icon"] = "display",
            ["image"] = "display",
            ["avatar"] = "display"
        };
}
