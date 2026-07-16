using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Aife.Application.Knowledge;
using Aife.Knowledge;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

#nullable disable warnings

// `train` subcommand: build/refresh a Knowledge Base from a source repo
// without needing Aife.Api/ASP.NET at all, e.g.:
//   aife-mcp train --source C:\path\to\core-repo --out .aife\knowledge --mode replace
if (args.Length > 0 && args[0].Equals("train", StringComparison.OrdinalIgnoreCase))
{
    return await RunTrainCommandAsync(args);
}

var knowledgePath = ResolveKnowledgePath(args);

var stdin = Console.OpenStandardInput();
var stdout = Console.OpenStandardOutput();
var stderr = Console.OpenStandardError();

// Write startup log to stderr (stdio is for JSON-RPC)
Log(stderr, $"Aife MCP Server v1.0.0 starting (kb: {knowledgePath})");

// Keep the Knowledge Base fresh automatically: if a live component-library
// source is configured, compare its fingerprint (git HEAD SHA, or a hash of
// file mtimes/sizes) against the one recorded at the last train and
// auto-retrain before serving any tool calls if it changed. If no source is
// configured at all, fall back to serving the static knowledge/ snapshot
// as-is (documented zero-config path) but warn that it may go stale.
var componentsSource = ResolveComponentsSourcePath(args);
if (componentsSource is not null)
{
    if (!IsGitUrl(componentsSource) && !Directory.Exists(componentsSource))
    {
        Log(stderr, $"FATAL: --components-source/AIFE_COMPONENTS_SOURCE points to a path that does not exist: {componentsSource}");
        return 1;
    }

    await RefreshKnowledgeIfStaleAsync(knowledgePath, componentsSource, stderr);
}
else
{
    Log(stderr, "WARNING: no --components-source/AIFE_COMPONENTS_SOURCE configured. " +
        "Serving the static knowledge/ snapshot as-is — it will not auto-refresh when " +
        "components change. Configure a source (or run `aife-mcp train` manually) to keep it current.");
}

var provider = new JsonKnowledgeProvider(knowledgePath);
var matchingService = new ComponentMatchingService(provider);
var tokenChecker = new TokenConformanceChecker(provider);
var scorer = new PrototypeScorer(provider);

while (true)
{
    try
    {
        var request = ReadJsonRpcMessage(stdin);
        if (request is null) break;

        var isNotification = request["id"] is null;
        Log(stderr, $"<- {request["method"]}{(isNotification ? " (notification)" : "")}");

        var response = await HandleRequestAsync(request, provider, matchingService, tokenChecker, scorer);

        // JSON-RPC notifications (no "id") must never receive a response —
        // sending one anyway breaks strict clients (e.g. VS Code's MCP host
        // treats it as a protocol violation and restarts the server,
        // appearing as an endless "starting..." loop).
        if (!isNotification)
            WriteJsonRpcMessage(stdout, response);

        Log(stderr, "-> ok");
    }
    catch (Exception ex)
    {
        Log(stderr, $"!! {ex.Message}");
        var error = new { jsonrpc = "2.0", error = new { code = -32603, message = ex.Message } };
        WriteJsonRpcMessage(stdout, error);
    }
}

return 0;

static async Task<int> RunTrainCommandAsync(string[] args)
{
    var source = GetArgValue(args, "--source");
    var outPath = GetArgValue(args, "--out");
    var modeArg = GetArgValue(args, "--mode") ?? "update";
    var branch = GetArgValue(args, "--branch");

    if (string.IsNullOrWhiteSpace(source))
    {
        Console.Error.WriteLine("Usage: aife-mcp train --source <repoPath|gitUrl> [--branch <name>] [--out <kbPath>] [--mode update|replace]");
        return 1;
    }

    outPath = string.IsNullOrWhiteSpace(outPath)
        ? Path.Combine(Directory.GetCurrentDirectory(), ".aife", "knowledge")
        : Path.GetFullPath(outPath);
    Directory.CreateDirectory(outPath);

    var mode = modeArg.Equals("replace", StringComparison.OrdinalIgnoreCase)
        ? TrainMode.Replace
        : TrainMode.Update;

    var trainer = new KnowledgeTrainerService(outPath);
    var result = IsGitUrl(source)
        ? await trainer.TrainFromGitAsync(source, branch, mode, CancellationToken.None)
        : await trainer.TrainFromFolderAsync(Path.GetFullPath(source), mode, CancellationToken.None);

    Console.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));
    return result.Success ? 0 : 1;
}

/// <summary>
/// True if the source string is a git remote (https/ssh URL or *.git) rather
/// than a local folder path — used to route to TrainFromGitAsync (clone then
/// scan) instead of TrainFromFolderAsync, and to skip local-path validation.
/// </summary>
static bool IsGitUrl(string source) =>
    source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
    source.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
    source.StartsWith("git@", StringComparison.OrdinalIgnoreCase) ||
    source.EndsWith(".git", StringComparison.OrdinalIgnoreCase);

static string? GetArgValue(string[] args, string flag)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }
    return null;
}

/// <summary>
/// Resolves the Knowledge Base directory so the MCP server can be installed
/// into (and run from within) any consumer project, not just this dev repo.
/// Priority order: explicit CLI arg, then env var, then a per-project
/// `.aife/knowledge` folder, then (dev-repo fallback) walking up from the
/// executable to find a `knowledge/` folder.
/// </summary>
static string ResolveKnowledgePath(string[] args)
{
    var cliPath = GetArgValue(args, "--knowledge");
    if (!string.IsNullOrWhiteSpace(cliPath))
        return Path.GetFullPath(cliPath);

    var envPath = Environment.GetEnvironmentVariable("AIFE_KNOWLEDGE_PATH");
    if (!string.IsNullOrWhiteSpace(envPath))
        return Path.GetFullPath(envPath);

    var projectLocalPath = Path.Combine(Directory.GetCurrentDirectory(), ".aife", "knowledge");
    if (Directory.Exists(projectLocalPath))
        return projectLocalPath;

    return Path.Combine(FindRepoRoot(), "knowledge");
}

/// <summary>
/// Resolves the live component-library source path (the "Core repo" containing
/// e.g. `src/Components/CustomUIs`) so the server can detect drift and
/// auto-refresh its Knowledge Base. Priority: explicit CLI arg, then env var.
/// Returns null if neither is configured (no auto-refresh; static snapshot only).
/// </summary>
static string? ResolveComponentsSourcePath(string[] args)
{
    var cliPath = GetArgValue(args, "--components-source");
    if (!string.IsNullOrWhiteSpace(cliPath))
        return IsGitUrl(cliPath) ? cliPath : Path.GetFullPath(cliPath);

    var envPath = Environment.GetEnvironmentVariable("AIFE_COMPONENTS_SOURCE");
    if (!string.IsNullOrWhiteSpace(envPath))
        return IsGitUrl(envPath) ? envPath : Path.GetFullPath(envPath);

    return null;
}

/// <summary>
/// Compares the current fingerprint of the component source folder against
/// the one recorded from the last successful train and, if different (or
/// none was recorded yet), re-runs the trainer in Update mode before the
/// server starts serving tool calls. Keeps steady-state startups fast when
/// nothing changed (fingerprint match -> no re-scan).
/// </summary>
static async Task RefreshKnowledgeIfStaleAsync(string knowledgePath, string componentsSource, Stream stderr)
{
    var fingerprintPath = Path.Combine(knowledgePath, ".source-fingerprint");
    var currentFingerprint = IsGitUrl(componentsSource)
        ? TryGetRemoteGitHeadSha(componentsSource) is { } sha ? $"git-remote:{sha}" : $"unknown:{Guid.NewGuid():N}"
        : ComputeSourceFingerprint(componentsSource);
    var previousFingerprint = File.Exists(fingerprintPath) ? await File.ReadAllTextAsync(fingerprintPath) : null;

    if (string.Equals(currentFingerprint, previousFingerprint, StringComparison.Ordinal))
    {
        Log(stderr, $"Components source unchanged since last train (source: {componentsSource}). Serving cached knowledge base.");
        return;
    }

    Log(stderr, $"Components source changed (or never trained) \u2014 refreshing knowledge base from {componentsSource}...");
    Directory.CreateDirectory(knowledgePath);
    var trainer = new KnowledgeTrainerService(knowledgePath);
    var result = IsGitUrl(componentsSource)
        ? await trainer.TrainFromGitAsync(componentsSource, null, TrainMode.Update, CancellationToken.None)
        : await trainer.TrainFromFolderAsync(componentsSource, TrainMode.Update, CancellationToken.None);

    if (!result.Success)
    {
        Log(stderr, $"WARNING: auto-refresh training reported errors: {string.Join("; ", result.Errors)}. Serving existing knowledge base as-is.");
        return;
    }

    await File.WriteAllTextAsync(fingerprintPath, currentFingerprint);
    Log(stderr, $"Knowledge base refreshed (componentsExtracted={result.ComponentsExtracted}, tokensExtracted={result.TokensExtracted}).");
}

/// <summary>
/// Fingerprints a component-library source folder so drift can be detected
/// cheaply on every startup. Prefers the git HEAD commit SHA (fast, exact)
/// when the source is a git working tree; falls back to a SHA-256 hash of
/// each relevant source file's relative path + size + last-write-time, which
/// still catches uncommitted local edits.
/// </summary>
static string ComputeSourceFingerprint(string sourcePath)
{
    var gitSha = TryGetGitHeadSha(sourcePath);
    if (gitSha is not null)
        return $"git:{gitSha}";

    var extensions = new HashSet<string> { ".tsx", ".jsx", ".ts", ".js", ".scss", ".css" };
    var excludedDirs = new HashSet<string> { "node_modules", "dist", "build", ".git", "bin", "obj" };

    var files = Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
        .Where(f => extensions.Contains(Path.GetExtension(f)))
        .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(excludedDirs.Contains))
        .OrderBy(f => f, StringComparer.Ordinal);

    var sb = new StringBuilder();
    foreach (var file in files)
    {
        var info = new FileInfo(file);
        var relative = Path.GetRelativePath(sourcePath, file);
        sb.Append(relative).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('\n');
    }

    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
    return $"hash:{Convert.ToHexString(hash)}";
}

static string? TryGetGitHeadSha(string sourcePath)
{
    if (!Directory.Exists(Path.Combine(sourcePath, ".git")))
        return null;

    try
    {
        var psi = new ProcessStartInfo("git", "rev-parse HEAD")
        {
            WorkingDirectory = sourcePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process is null) return null;

        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit(5000);
        return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output : null;
    }
    catch
    {
        // git not installed, not a repo, or any other failure -> fall back to hash-based fingerprint.
        return null;
    }
}

/// <summary>
/// Fingerprints a remote git source without a full clone, via `git ls-remote
/// &lt;url&gt; HEAD` (lightweight network call). Returns null if ls-remote
/// fails (offline, auth required, etc.) so the caller falls back to a random
/// fingerprint that always forces a retrain rather than silently skipping one.
/// </summary>
static string? TryGetRemoteGitHeadSha(string gitUrl)
{
    try
    {
        var psi = new ProcessStartInfo("git", $"ls-remote {gitUrl} HEAD")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process is null) return null;

        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit(10000);
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            return null;

        // Output format: "<sha>\tHEAD"
        return output.Split('\t', ' ')[0];
    }
    catch
    {
        return null;
    }
}

static async Task<object> HandleRequestAsync(
    JObject request,
    IKnowledgeProvider provider,
    ComponentMatchingService matchingService,
    TokenConformanceChecker tokenChecker,
    PrototypeScorer scorer)
{
    var method = request["method"]?.ToString() ?? "";
    var id = request["id"];
    var ct = CancellationToken.None;

    try
    {
        return method switch
        {
            "initialize" => new { jsonrpc = "2.0", id, result = new { protocolVersion = ResolveProtocolVersion(request), serverInfo = new { name = "aife-mcp", version = "1.0.0" }, capabilities = new { tools = new { listChanged = false } } } },
            "tools/list" => new { jsonrpc = "2.0", id, result = new { tools = Aife.Mcp.McpToolDefinitions.Tools } },
            "tools/call" => new { jsonrpc = "2.0", id, result = await CallToolAsync(request["params"]?["name"]?.ToString() ?? "", request["params"]?["arguments"] as JObject, provider, matchingService, tokenChecker, scorer, ct) },
            "notifications/initialized" => new { jsonrpc = "2.0", id, result = new { } },
            "ping" => new { jsonrpc = "2.0", id, result = new { } },
            // We only declared the "tools" capability, but some clients still
            // probe resources/prompts during discovery regardless. Answer
            // with empty lists rather than an error so discovery can't get
            // stuck on an unadvertised-but-queried capability.
            "resources/list" => new { jsonrpc = "2.0", id, result = new { resources = Array.Empty<object>() } },
            "prompts/list" => new { jsonrpc = "2.0", id, result = new { prompts = Array.Empty<object>() } },
            _ => new { jsonrpc = "2.0", id, error = new { code = -32601, message = $"Unknown method: {method}" } }
        };
    }
    catch (Exception ex)
    {
        return new { jsonrpc = "2.0", id, error = new { code = -32000, message = ex.Message } };
    }
}

/// <summary>
/// Echoes back the protocol version the client requested during
/// "initialize" rather than a hardcoded value. Some strict MCP clients
/// (e.g. VS Code's built-in MCP host) will refuse to complete the handshake
/// — appearing to hang on "starting..." with no error — if the server
/// responds with a version the client doesn't recognize/negotiate. Since
/// this server's actual wire format hasn't changed across MCP revisions,
/// agreeing to whatever the client asked for maximizes compatibility.
/// </summary>
static string ResolveProtocolVersion(JObject request) =>
    request["params"]?["protocolVersion"]?.ToString() ?? "2025-03-26";


static async Task<object> CallToolAsync(
    string toolName,
    JObject? args,
    IKnowledgeProvider provider,
    ComponentMatchingService matchingService,
    TokenConformanceChecker tokenChecker,
    PrototypeScorer scorer,
    CancellationToken ct)
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
        "match_element" => await matchingService.MatchElementAsync(args?["kind"]?.ToString() ?? "", args?["text"]?.ToString(), ct),
        "check_token_conformance" => await tokenChecker.CheckAsync(args?["css"]?.ToString() ?? "", ct),
        "score_prototype" => await scorer.ScoreAsync(
            args?["elements"]?.ToObject<List<ScoredElementInput>>() ?? new List<ScoredElementInput>(),
            args?["tokenViolations"]?.ToObject<List<TokenViolation>>() ?? new List<TokenViolation>(),
            ct),
        _ => new { error = $"Unknown tool: {toolName}" }
    };
}

static JObject? ReadJsonRpcMessage(Stream stdin)
{
    // MCP's stdio transport frames messages as newline-delimited JSON (one
    // JSON-RPC message per line, no headers) — NOT the LSP-style
    // "Content-Length: N\r\n\r\n" framing this used to (incorrectly) expect.
    // That mismatch made the server silently hang forever inside the old
    // header-parsing loop, waiting for a blank line that would never arrive,
    // since real clients (VS Code, etc.) never send one.
    var line = ReadLine(stdin);
    if (string.IsNullOrEmpty(line)) return null;
    return JObject.Parse(line);
}

static void WriteJsonRpcMessage(Stream stdout, object message)
{
    var json = JsonConvert.SerializeObject(message, Formatting.None);
    var bytes = Encoding.UTF8.GetBytes(json + "\n");
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
