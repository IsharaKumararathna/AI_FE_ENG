# MCP Server — VS Code Extension Integration

Status: Approved · Date: 2026-07-14 · Version: 1.0

## Summary

The Aife MCP (`src/Aife.Mcp`) is an MCP-compliant server that exposes the
Design System Knowledge Base as 11 tools. VS Code connects to it via the
**MCP Server Host** extension, making DS knowledge available to GitHub Copilot
and other MCP-aware tools.

## Prerequisites

- .NET 8 SDK installed
- VS Code

## Step 1 — Build the MCP Server

```powershell
cd C:\99xProjectDir\Research\AI_FE_ENG
dotnet build src\Aife.Mcp --nologo -v q
```

This produces `src/Aife.Mcp/bin/Debug/net8.0/Aife.Mcp.dll`.

## Step 2 — Install the MCP Server Host Extension

1. Open VS Code
2. Press `Ctrl+Shift+X` to open the Extensions sidebar
3. Search for **"MCP Server Host"** (by GitHub)
4. Click **Install**

## Step 3 — Configure `mcp.json`

Create or edit `.vscode/mcp.json` in your workspace:

```json
{
    "servers": {
        "aife-design-system": {
            "type": "stdio",
            "command": "dotnet",
            "args": [
                "run",
                "--project",
                "src/Aife.Mcp",
                "--no-build"
            ],
            "cwd": "C:\\99xProjectDir\\Research\\AI_FE_ENG"
        }
    }
}
```

> **Alternative — run the compiled DLL directly (faster startup):**
>
> ```json
> {
>     "servers": {
>         "aife-design-system": {
>             "type": "stdio",
>             "command": "dotnet",
>             "args": [
>                 "C:\\99xProjectDir\\Research\\AI_FE_ENG\\src\\Aife.Mcp\\bin\\Debug\\net8.0\\Aife.Mcp.dll"
>             ]
>         }
>     }
> }
> ```

## Step 4 — Verify

1. Open VS Code Command Palette (`Ctrl+Shift+P`)
2. Run **"MCP: List Servers"** — you should see `aife-design-system` with status **Running**
3. Run **"MCP: List Tools"** — you should see 11 tools listed:

| Tool | Description |
|---|---|
| `search_components` | Search component catalog by category/status/text |
| `get_component` | Get full detail for a component |
| `get_component_props` | Get component props/API |
| `get_component_examples` | Get usage examples |
| `get_layout_patterns` | Get all approved layout patterns |
| `get_design_tokens` | Get all design tokens |
| `get_icons` | Search available icons |
| `search_components_by_description` | Natural language component search |
| `get_best_practices` | Get DS best practices |
| `get_accessibility_rules` | Get accessibility rules |
| `get_reference_ui_patterns` | Get reference UI patterns (ADR-005) |

## How Copilot Uses It

When the MCP server is running, GitHub Copilot can call these tools automatically
when you ask design system questions. For example:

> **You:** "What button component should I use for a primary action?"

Copilot will call `search_components_by_description` with your query and return
the matching `BUSButton` component with its variants and props.

> **You:** "Show me the color tokens available."

Copilot will call `get_design_tokens` and list all color, spacing, radius,
shadow, and typography tokens.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Server shows "Stopped" | Check that `dotnet build src/Aife.Mcp` succeeds |
| "Method not found" error | The server only supports MCP protocol methods — check `mcp.json` syntax |
| No tools listed | Ensure `knowledge/` folder exists and `manifest.json` is valid |
| Copilot doesn't call tools | Ensure the MCP Server Host extension is installed and the server is Running |

## Protocol

The server implements **MCP 2025-03-26** over **stdio transport** using JSON-RPC 2.0.
Messages are framed with `Content-Length` headers.

## Related

- `Docs/04-cross-cutting/01-mcp-server-design.md`: MCP tool definitions
- `Docs/00-foundation/08-mcp-architecture.md`: MCP architecture and contract equivalence
- `src/Aife.Mcp/Program.cs`: Server implementation
- `src/Aife.Mcp/McpToolDefinitions.cs`: Tool schema definitions
