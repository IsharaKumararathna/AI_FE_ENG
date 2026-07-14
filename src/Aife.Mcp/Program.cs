using System.Text;
using Aife.Application.Knowledge;
using Aife.Knowledge;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

#nullable disable warnings

var repoRoot = FindRepoRoot();
var knowledgePath = Path.Combine(repoRoot, "knowledge");
var provider = new JsonKnowledgeProvider(knowledgePath);

var stdin = Console.OpenStandardInput();
var stdout = Console.OpenStandardOutput();
var stderr = Console.OpenStandardError();

// Write startup log to stderr (stdio is for JSON-RPC)
Log(stderr, $"Aife MCP Server v1.0.0 starting (kb: {knowledgePath})");

while (true)
{
    try
    {
        var request = ReadJsonRpcMessage(stdin);
        if (request is null) break;

        Log(stderr, $"<- {request["method"]}");
        var response = await HandleRequestAsync(request, provider);
        WriteJsonRpcMessage(stdout, response);
        Log(stderr, $"-> ok");
    }
    catch (Exception ex)
    {
        Log(stderr, $"!! {ex.Message}");
        var error = new { jsonrpc = "2.0", error = new { code = -32603, message = ex.Message } };
        WriteJsonRpcMessage(stdout, error);
    }
}

static async Task<object> HandleRequestAsync(JObject request, IKnowledgeProvider provider)
{
    var method = request["method"]?.ToString() ?? "";
    var id = request["id"];
    var ct = CancellationToken.None;

    try
    {
        return method switch
        {
            "initialize" => new { jsonrpc = "2.0", id, result = new { protocolVersion = "2025-03-26", serverInfo = new { name = "aife-mcp", version = "1.0.0" }, capabilities = new { tools = new { } } } },
            "tools/list" => new { jsonrpc = "2.0", id, result = new { tools = Aife.Mcp.McpToolDefinitions.Tools } },
            "tools/call" => new { jsonrpc = "2.0", id, result = await CallToolAsync(request["params"]?["name"]?.ToString() ?? "", request["params"]?["arguments"] as JObject, provider, ct) },
            "notifications/initialized" => new { jsonrpc = "2.0", id, result = new { } },
            _ => new { jsonrpc = "2.0", id, error = new { code = -32601, message = $"Unknown method: {method}" } }
        };
    }
    catch (Exception ex)
    {
        return new { jsonrpc = "2.0", id, error = new { code = -32000, message = ex.Message } };
    }
}

static async Task<object> CallToolAsync(string toolName, JObject? args, IKnowledgeProvider provider, CancellationToken ct)
{
    return toolName switch
    {
        "search_components" => await provider.SearchComponentsAsync(args?.ToObject<ComponentQuery>() ?? new ComponentQuery(), ct),
            "get_component" => await provider.GetComponentAsync(args?["componentId"]?.ToString() ?? "", ct) as object ?? new { error = "Component not found" },
        "get_component_props" => await provider.GetComponentPropsAsync(args?["componentId"]?.ToString() ?? "", ct),
        "get_component_examples" => await provider.GetComponentExamplesAsync(args?["componentId"]?.ToString() ?? "", ct),
        "get_layout_patterns" => await provider.GetLayoutPatternsAsync(ct),
        "get_design_tokens" => await provider.GetDesignTokensAsync(ct),
        "get_icons" => await provider.GetIconsAsync(args?.ToObject<IconQuery>() ?? new IconQuery(), ct),
        "search_components_by_description" => await provider.SearchComponentsByDescriptionAsync(args?["description"]?.ToString() ?? "", ct),
        "get_best_practices" => await provider.GetBestPracticesAsync(args?.ToObject<BestPracticeQuery>() ?? new BestPracticeQuery(), ct),
        "get_accessibility_rules" => await provider.GetAccessibilityRulesAsync(ct),
        "get_reference_ui_patterns" => await provider.GetReferenceUiPatternsAsync(ct),
        _ => new { error = $"Unknown tool: {toolName}" }
    };
}

static JObject? ReadJsonRpcMessage(Stream stdin)
{
    var contentLength = 0;
    while (true)
    {
        var line = ReadLine(stdin);
        if (string.IsNullOrEmpty(line)) break;
        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            contentLength = int.Parse(line.AsSpan(14).Trim());
    }
    if (contentLength <= 0) return null;
    var buffer = new byte[contentLength];
    var totalRead = 0;
    while (totalRead < contentLength) { var r = stdin.Read(buffer, totalRead, contentLength - totalRead); if (r == 0) break; totalRead += r; }
    return JObject.Parse(Encoding.UTF8.GetString(buffer, 0, totalRead));
}

static void WriteJsonRpcMessage(Stream stdout, object message)
{
    var json = JsonConvert.SerializeObject(message, Formatting.None);
    var bytes = Encoding.UTF8.GetBytes(json);
    var header = $"Content-Length: {bytes.Length}\r\n\r\n";
    stdout.Write(Encoding.UTF8.GetBytes(header));
    stdout.Write(bytes);
    stdout.Flush();
}

static string? ReadLine(Stream stream)
{
    var sb = new StringBuilder();
    while (true) { var b = stream.ReadByte(); if (b == -1 || b == '\n') break; if (b != '\r') sb.Append((char)b); }
    return sb.ToString();
}

static void Log(Stream stderr, string msg) { var bytes = Encoding.UTF8.GetBytes($"[aife-mcp] {msg}\n"); stderr.Write(bytes); stderr.Flush(); }

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null) { if (Directory.Exists(Path.Combine(dir.FullName, "knowledge"))) return dir.FullName; dir = dir.Parent; }
    return AppContext.BaseDirectory;
}
