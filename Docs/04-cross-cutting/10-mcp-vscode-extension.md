# MCP Server — VS Code Extension Integration

Status: Approved · Date: 2026-07-15 · Version: 2.3
Updated: 2026-07-15 — Fixed the actual root cause of the server hanging
forever on "starting": it used LSP-style `Content-Length:` header framing
instead of the MCP stdio transport's real newline-delimited JSON framing.
Confirmed via VS Code's own persisted MCP server log
(`%APPDATA%\Code\logs\<session>\window*\mcpServer.mcp.config.usrlocal.aife-design-system.log`),
which showed VS Code endlessly logging "Waiting for server to respond to
`initialize` request..." — the server was stuck inside its (incorrect)
header-parsing loop and never even read the request.

## Summary

The Aife MCP (`src/Aife.Mcp`) is an MCP-compliant server that exposes the
Design System Knowledge Base as 14 tools — 11 read-only KB lookups plus 3
deterministic (no-LLM) tools for matching prototype elements to real
components and scoring conformance (`match_element`, `check_token_conformance`,
`score_prototype`). VS Code (1.99+) has **built-in MCP client support** as
part of GitHub Copilot Chat's Agent mode — no separate marketplace
extension is required to connect to it.

As of v2.0 the server is **portable**: it can be installed into (and its
Knowledge Base trained from) any consumer project, not just this dev repo —
see "Installing in a Consumer Project" below.

## Prerequisites

- .NET 8 SDK installed
- VS Code 1.99+ with the GitHub Copilot Chat extension (Agent mode)

## Step 1 — Build the MCP Server

```powershell
cd C:\99xProjectDir\Research\AI_FE_ENG
dotnet build src\Aife.Mcp --nologo -v q
```

This produces `src/Aife.Mcp/bin/Debug/net8.0/Aife.Mcp.dll`.

## Step 2 — Confirm MCP support is available

Open the Command Palette (`Ctrl+Shift+P`) and check that **"MCP: List
Servers"** (or **"MCP: Add Server"**) exists. If neither command exists,
update VS Code and the GitHub Copilot Chat extension to the latest version —
MCP support is built into Copilot Chat's Agent mode, not a separate
marketplace extension.

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

There is no dedicated "list tools" command — verify through the server entry
and Copilot Chat's tool picker instead:

1. Open the Command Palette (`Ctrl+Shift+P`) and run **"MCP: List Servers"**.
   `aife-design-system` should appear; selecting it offers **Start/Restart**,
   **Show Output** (server stderr log — useful for troubleshooting), and
   similar actions. If it shows an error/stopped state, use **Show Output**
   first to see the server's startup log line
   (`Aife MCP Server v1.0.0 starting (kb: ...)` on success).
2. Open **Copilot Chat**, switch to **Agent mode**, and click the **Tools**
   (wrench) icon above the chat input. `aife-design-system` should be listed
   as a tool group; expanding it shows all 14 tools below.
3. As a smoke test, ask Copilot something that requires a tool call, e.g.
   *"Using the aife-design-system tools, what components map to a 'table'
   HTML element?"* — Copilot should invoke `match_element` (you'll see a
   tool-call card in the chat) and answer using the real result.

The 14 tools you should see:

| Tool | Description |
|---|---|
| `search_components` | Search component catalog by category/status/text |
| `get_component` | Get full detail for a component (incl. importPath/exportName/isDefaultExport) |
| `get_component_props` | Get component props/API |
| `get_component_examples` | Get usage examples |
| `get_layout_patterns` | Get all approved layout patterns |
| `get_design_tokens` | Get all design tokens |
| `get_icons` | Search available icons |
| `search_components_by_description` | Natural language component search |
| `get_best_practices` | Get DS best practices |
| `get_accessibility_rules` | Get accessibility rules |
| `get_reference_ui_patterns` | Get reference UI patterns (ADR-005) |
| `match_element` | Deterministically match a detected HTML element to ranked component candidates |
| `check_token_conformance` | Deterministically flag hardcoded colors/spacing in CSS |
| `score_prototype` | Combine match/token/accessibility coverage into one 0-100 score |

## Installing in a Consumer Project

The MCP server no longer requires living inside this dev repo. Any project
can register it and train its own local Knowledge Base from its own
component library (the "Core repo").

### 1. Publish a self-contained executable

From this repo, publish `Aife.Mcp` as a single portable file (pick the RID for
your OS — `win-x64`, `linux-x64`, `osx-arm64`, etc.):

```powershell
dotnet publish src/Aife.Mcp -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o C:\tools\aife-mcp
```

This produces `C:\tools\aife-mcp\Aife.Mcp.exe` — copy it (or the whole output
folder) anywhere; it has no dependency on this repo or a .NET runtime install.

### 2. Train a local Knowledge Base from the consumer project's Core repo

Run the `train` subcommand once (and again any time components change) to
scan the real component library and build a `.aife/knowledge` folder:

```powershell
C:\tools\aife-mcp\Aife.Mcp.exe train --source "C:\path\to\CoreRepo" --out "C:\path\to\ConsumerProject\.aife\knowledge" --mode replace
```

- `--source` — path to the repo containing the real component library (e.g. the
  folder with `src/Components/CustomUIs`).
- `--out` — where to write the Knowledge Base. Defaults to `.aife/knowledge`
  under the current directory if omitted.
- `--mode` — `update` (default, merges with existing KB) or `replace` (wipes
  and rebuilds components from scratch; tokens are always merged/updated).

This has no ASP.NET/Aife.Api dependency — it's a plain console invocation.

### 3. Register the server in the consumer project's `.vscode/mcp.json`

```json
{
    "servers": {
        "aife-design-system": {
            "type": "stdio",
            "command": "C:\\tools\\aife-mcp\\Aife.Mcp.exe",
            "env": {
                "AIFE_KNOWLEDGE_PATH": "${workspaceFolder}\\.aife\\knowledge"
            }
        }
    }
}
```

### Knowledge path resolution order

The server resolves its Knowledge Base directory in this priority order, so
the same published executable works across projects without rebuilding:

1. `--knowledge <path>` CLI argument (in `mcp.json`'s `args`)
2. `AIFE_KNOWLEDGE_PATH` environment variable (shown above)
3. `<current working directory>/.aife/knowledge`
4. Dev-repo fallback: walks up from the executable's folder looking for a
   `knowledge/` directory (this is what makes Steps 1-3 above work unchanged
   for this repo without any extra configuration)

> **Out of scope for now:** a fully automated `.vsix` VS Code extension that
> auto-prompts for the Core repo path, auto-runs `train`, and auto-writes
> `mcp.json` on install. The manual publish + `train` + `mcp.json` steps above
> are the supported path today; a one-click installer is future work.

## How Copilot Uses It

When the MCP server is running, GitHub Copilot can call these tools automatically
when you ask design system questions. For example:

> **You:** "What button component should I use for a primary action?"

Copilot will call `search_components_by_description` with your query and return
the matching `BUSButton` component with its variants and props.

> **You:** "Show me the color tokens available."

Copilot will call `get_design_tokens` and list all color, spacing, radius,
shadow, and typography tokens.

## Host-Agent Workflow: Turning a Rough Prototype into Real Code

This is the exact tool-call sequence a host coding agent should follow when a
non-technical user hands it a rough/incomplete HTML+CSS prototype and expects
back interactive React built from the project's real components — see
[handling-unknown-elements.md](../02-ai-modules/07-handling-unknown-elements.md)
for the full worked example and how this differs from the legacy HTTP flows.

1. **Read the HTML/CSS directly** (no upload API call needed — the agent
   already has the file in context).
2. **For each detected element**, call `match_element({ kind, text? })`.
   - Prefer candidates with `confidence >= 0.5`.
   - An **empty result is normal** — it means no approved component covers
     this element. Do not invent a component or a plausible-looking import;
     track it as unmatched instead.
3. **Assemble the page** using only matched candidates, and write the
   `.jsx`/`.tsx` file **directly into the project's real source folder**
   using each candidate's exact `importPath`/`exportName`/`isDefaultExport`
   — never a fabricated package name like `@org/ds/react`.
4. **Call `check_token_conformance({ css })`** on the prototype's CSS to get
   hardcoded color/spacing violations and a token sub-score.
5. **Call `score_prototype({ elements, tokenViolations })`** with every
   element's best match (`kind`, `text`, `matchedComponentId`, `confidence`)
   and the violations from step 4.
6. **Present the user**: the file path you wrote, the `finalScore` with its
   `matchScore`/`tokenScore`/`a11yScore` breakdown, and the
   `unmatchedElements`/`suggestions` so they know exactly what (if anything)
   still needs a human decision — instead of having to pre-tune the HTML
   before ever finding out.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Server shows "Running" but the client waits forever on the `initialize` handshake ("starting..." never resolves) | **This was the real root cause of a hang, now fixed**: the server used LSP-style `Content-Length: N\r\n\r\n` header framing, but the actual MCP stdio transport uses plain newline-delimited JSON (no headers). The server's read loop got stuck forever waiting for a header line real clients never send. To diagnose this class of issue directly, check VS Code's own persisted server log at `%APPDATA%\Code\logs\<latest-session>\window*\mcpServer.mcp.config.usrlocal.aife-design-system.log` — a repeating `Waiting for server to respond to 'initialize' request...` line with no earlier response is the signature of this bug. |
| Server keeps restarting / "starting..." loops endlessly in the chat until you dismiss it | Also fixed: the server used to send a response to the `notifications/initialized` message, which JSON-RPC forbids for notifications (no `id`). Strict MCP clients treat that as a protocol violation and restart the server repeatedly. |
| Rebuild fails with the DLL locked by another process | VS Code auto-restarts the server (~1s) whenever it exits, immediately re-locking the DLL. Loop `Stop-Process` + `dotnet build` a few times, or close the VS Code window while rebuilding. |
| Server shows "Stopped"/error in "MCP: List Servers" | Select it → **Show Output**, or read the persisted log file described above, to see the server's stderr log; check that `dotnet build src/Aife.Mcp` succeeds first |
| "Method not found" error | The server only supports MCP protocol methods — check `mcp.json` syntax |
| No tools shown in Copilot's Tools picker | Ensure the resolved Knowledge Base folder exists and its `manifest.json` is valid (check the server's stderr log for the `kb: <path>` it resolved) |
| Wrong/empty Knowledge Base in a consumer project | Check the resolution order below — an env var or `.aife/knowledge` folder from a previous project can shadow the intended one |
| Copilot doesn't call tools | Confirm you're in **Agent mode** (not plain chat) and that `aife-design-system` is enabled in the Tools picker |

## Protocol

The server implements **MCP 2025-03-26+** over **stdio transport** using
JSON-RPC 2.0 framed as **newline-delimited JSON** — one complete JSON object
per line, terminated by `\n`, with no headers. (An earlier version of this
server incorrectly used LSP-style `Content-Length: N\r\n\r\n` framing, which
made every real client hang forever on the `initialize` handshake since no
real MCP client sends that header. Fixed 2026-07-15 — see the version log
above and `src/Aife.Mcp/Program.cs`'s `ReadJsonRpcMessage`/`WriteJsonRpcMessage`.)

## Related

- `Docs/04-cross-cutting/01-mcp-server-design.md`: MCP tool definitions
- `Docs/00-foundation/08-mcp-architecture.md`: MCP architecture and contract equivalence
- `src/Aife.Mcp/Program.cs`: Server implementation
- `src/Aife.Mcp/McpToolDefinitions.cs`: Tool schema definitions
