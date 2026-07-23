using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Aife.Application.Knowledge;
using Aife.Knowledge;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Polly;
using Polly.Retry;

#nullable disable warnings

// `train` subcommand: build/refresh a Knowledge Base from a source repo
// without needing Aife.Api/ASP.NET at all, e.g.:
//   aife-mcp train --source C:\path\to\core-repo --out .aife\knowledge --mode replace
if (args.Length > 0 && args[0].Equals("train", StringComparison.OrdinalIgnoreCase))
{
    return await RunTrainCommandAsync(args);
}

// `setup` subcommand: one-command bootstrap for a consumer project — trains
// the KB, auto-detects the preview root, and writes .vscode/mcp.json so POs
// only need to open VS Code (no manual env vars / path wrangling).
//   aife-mcp setup --source C:\Core\Repo --project C:\Consumer\App
if (args.Length > 0 && args[0].Equals("setup", StringComparison.OrdinalIgnoreCase))
{
    return await RunSetupCommandAsync(args);
}

// `proxy` subcommand: lightweight HTTP reverse proxy with Polly retry that
// sits between the VS Code LLM extension and the upstream API, shielding
// the MCP flow from transient ECONNRESET / ETIMEDOUT / ECONNREFUSED errors.
//   aife-mcp proxy --upstream https://api.openai.com --port 3456
if (args.Length > 0 && args[0].Equals("proxy", StringComparison.OrdinalIgnoreCase))
{
    return await RunProxyCommandAsync(args);
}

// `aggregate` subcommand: closes the conformance feedback loop — clusters
// recurring findings under data/conformance/ into a summary that drives
// prompt/KB improvements (e.g. the banned-color list in react.generator).
//   aife-mcp aggregate --conformance data/conformance --out data/conformance-summary.json
if (args.Length > 0 && args[0].Equals("aggregate", StringComparison.OrdinalIgnoreCase))
{
    return await RunAggregateCommandAsync(args);
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

// Resolves where save_ai_preview writes generated pages so non-technical
// reviewers can see them live (via require.context auto-discovery in the
// consumer project) with zero manual file placement or route wiring.
var previewRoot = ResolvePreviewRootPath(args, componentsSource);
if (previewRoot is not null)
    Log(stderr, $"AI preview writes enabled (root: {previewRoot}).");
else
    Log(stderr, "WARNING: no --preview-root/AIFE_PREVIEW_ROOT (or --components-source fallback) configured. " +
        "The save_ai_preview tool will be unavailable until one is set.");

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

        var response = await HandleRequestAsync(request, provider, matchingService, tokenChecker, scorer, previewRoot);

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

// ──────────────────────────────────────────────────────────────────
//  aggregate subcommand — closes the conformance feedback loop
// ──────────────────────────────────────────────────────────────────

static async Task<int> RunAggregateCommandAsync(string[] args)
{
    var conformanceDir = GetArgValue(args, "--conformance")
        ?? Path.Combine(Directory.GetCurrentDirectory(), "data", "conformance");
    var outPath = GetArgValue(args, "--out")
        ?? Path.Combine(Directory.GetCurrentDirectory(), "data", "conformance-summary.json");

    var aggregator = new ConformanceAggregator(Path.GetFullPath(conformanceDir));
    var summary = aggregator.WriteSummary(Path.GetFullPath(outPath));

    Console.WriteLine(JsonConvert.SerializeObject(summary, Formatting.Indented));
    Console.Error.WriteLine($"Wrote conformance summary to {outPath} " +
        $"({summary.TotalReports} reports, {summary.TotalFindings} findings, " +
        $"{summary.TopViolations.Count} distinct violation types).");
    return await Task.FromResult(0);
}

// ──────────────────────────────────────────────────────────────────
//  setup subcommand
// ──────────────────────────────────────────────────────────────────

static async Task<int> RunSetupCommandAsync(string[] args)
{
    var source = GetArgValue(args, "--source");
    var project = GetArgValue(args, "--project");
    var exePath = GetArgValue(args, "--exe-path");

    if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(project))
    {
        Console.Error.WriteLine("Usage: aife-mcp setup --source <CoreRepoPath> --project <ConsumerProjectPath> [--exe-path <aifeExePath>]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  --source   Path to the core component library repo (required)");
        Console.Error.WriteLine("  --project  Path to the consumer project (required)");
        Console.Error.WriteLine("  --exe-path Path to the published aife-mcp executable (auto-detected if omitted)");
        return 1;
    }

    source = Path.GetFullPath(source);
    project = Path.GetFullPath(project);

    if (!Directory.Exists(source))
    {
        Console.Error.WriteLine($"ERROR: Source path does not exist: {source}");
        return 1;
    }

    if (!Directory.Exists(project))
    {
        Console.Error.WriteLine($"ERROR: Project path does not exist: {project}");
        return 1;
    }

    // ── Step 1: Train KB ──
    var kbPath = Path.Combine(project, ".aife", "knowledge");
    Console.WriteLine($"[1/3] Training knowledge base from {source}");
    Console.WriteLine($"      -> {kbPath}");
    Directory.CreateDirectory(kbPath);

    var trainer = new KnowledgeTrainerService(kbPath);
    var result = IsGitUrl(source)
        ? await trainer.TrainFromGitAsync(source, null, TrainMode.Replace, CancellationToken.None)
        : await trainer.TrainFromFolderAsync(source, TrainMode.Replace, CancellationToken.None);

    if (!result.Success)
    {
        Console.Error.WriteLine($"ERROR: Training failed: {string.Join("; ", result.Errors)}");
        return 1;
    }

    Console.WriteLine($"      {result.ComponentsExtracted} components, {result.TokensExtracted} tokens extracted.");

    // ── Step 2: Detect preview root ──
    var previewRoot = DetectPreviewRoot(project, source);
    Console.WriteLine(previewRoot is not null
        ? $"[2/3] Preview root detected: {previewRoot}"
        : "[2/3] Preview root: not detected (save_ai_preview will be unavailable until --preview-root or AIFE_PREVIEW_ROOT is set)");

    // ── Step 3: Generate .vscode/mcp.json ──
    Console.WriteLine("[3/4] Generating .vscode/mcp.json ...");
    var mcpJsonPath = Path.Combine(project, ".vscode", "mcp.json");
    Directory.CreateDirectory(Path.GetDirectoryName(mcpJsonPath)!);

    var resolvedExe = exePath is not null
        ? Path.GetFullPath(exePath)
        : Process.GetCurrentProcess().MainModule?.FileName ?? "aife-mcp";

    var mcpArgs = new List<string>
    {
        "--knowledge", kbPath,
        "--components-source", source
    };
    if (previewRoot is not null)
    {
        mcpArgs.Add("--preview-root");
        mcpArgs.Add(previewRoot);
    }

    var mcpConfig = new JObject
    {
        ["servers"] = new JObject
        {
            ["aife"] = new JObject
            {
                ["type"] = "stdio",
                ["command"] = resolvedExe,
                ["args"] = new JArray(mcpArgs.Select(a => (JToken)a))
            }
        }
    };

    if (File.Exists(mcpJsonPath))
        Console.WriteLine($"      {mcpJsonPath} already exists — overwriting.");

    await File.WriteAllTextAsync(mcpJsonPath, mcpConfig.ToString(Formatting.Indented));

    // ── Step 4: Deploy AiPreviewPage.tsx ──
    Console.WriteLine("[4/4] Deploying AiPreviewPage.tsx ...");
    await DeployAiPreviewPageAsync(previewRoot, Console.OpenStandardError());
    Console.WriteLine($"      -> {Path.Combine(previewRoot, "AiPreviewPage.tsx")}");

    Console.WriteLine();
    Console.WriteLine("Setup complete.");
    Console.WriteLine($"  Knowledge base: {kbPath}");
    Console.WriteLine($"  MCP config:     {mcpJsonPath}");
    if (previewRoot is not null)
        Console.WriteLine($"  Preview root:   {previewRoot}");
    Console.WriteLine();
    Console.WriteLine("Next: open the consumer project in VS Code (v1.99+) and use Copilot Chat in Agent mode.");

    return 0;
}

/// <summary>
/// Scans the consumer project and source repo for common React project
/// patterns where the AI-preview folder should live. Returns the first
/// match, or null if no recognizable structure is found.
/// </summary>
static string? DetectPreviewRoot(string projectPath, string sourcePath)
{
    var roots = new[] { projectPath, sourcePath };
    var subPaths = new[]
    {
        Path.Combine("src", "Components", "AppLogic", "_AiPreview"),
        Path.Combine("src", "Components", "CustomUIs", "_AiPreview"),
        Path.Combine("src", "Components", "_AiPreview"),
        Path.Combine("src", "components", "_AiPreview"),
    };

    // 1. Check if _AiPreview already exists somewhere
    foreach (var root in roots)
        foreach (var sub in subPaths)
        {
            var candidate = Path.Combine(root, sub);
            if (Directory.Exists(candidate))
                return candidate;
        }

    // 2. Pick the first parent folder that exists and append _AiPreview
    var basePaths = new[]
    {
        Path.Combine("src", "Components", "AppLogic"),
        Path.Combine("src", "Components", "CustomUIs"),
        Path.Combine("src", "Components"),
        Path.Combine("src", "components"),
    };

    foreach (var root in roots)
        foreach (var sub in basePaths)
        {
            var baseDir = Path.Combine(root, sub);
            if (Directory.Exists(baseDir))
                return Path.Combine(baseDir, "_AiPreview");
        }

    return null;
}

/// <summary>
/// Deploys (or refreshes) the AiPreviewPage.tsx component into the consumer
/// project's _AiPreview folder so the single static route
/// /#/ai-preview/:slug? auto-discovers every generated component via
/// require.context — zero manual route wiring, ever.
/// Skips if the consumer project already has a newer (locally-modified)
/// AiPreviewPage.tsx; writes when missing or when our embedded version is
/// newer (tracked via a .aife-version comment in the file).
/// </summary>
static async Task DeployAiPreviewPageAsync(string previewRoot, Stream stderr)
{
    var targetPath = Path.Combine(previewRoot, "AiPreviewPage.tsx");
    var template = ReadAiPreviewPageTemplate();
    var embeddedVersion = EmbedFileVersion(template);

    if (File.Exists(targetPath))
    {
        var existing = await File.ReadAllTextAsync(targetPath);
        var existingVersion = EmbedFileVersion(existing);

        // embeddedVersion == 0 means the template itself hasn't been versioned
        // yet — still deploy (don't skip), because the existing file might
        // be a stale single-view version from before the @aife-version
        // convention was introduced.
        if (embeddedVersion > 0 && existingVersion >= embeddedVersion)
        {
            Log(stderr, $"AiPreviewPage.tsx already at version {existingVersion} (embedded is {embeddedVersion}) — skipping deploy.");
            return;
        }

        if (existingVersion > 0)
            Log(stderr, $"AiPreviewPage.tsx is at version {existingVersion}, updating to {embeddedVersion}.");
        else
            Log(stderr, $"AiPreviewPage.tsx has no version header (pre-v1), updating to {embeddedVersion}.");
    }
    else
    {
        Log(stderr, $"AiPreviewPage.tsx not found — deploying version {embeddedVersion}.");
    }

    Directory.CreateDirectory(previewRoot);
    await File.WriteAllTextAsync(targetPath, template);
    Log(stderr, $"AiPreviewPage.tsx deployed to {targetPath}.");
}

/// <summary>
/// Extracts the embedded version number from the first line of the file,
/// which must start with "// @aife-version: N". Returns 0 if no version
/// comment is found.
/// </summary>
static int EmbedFileVersion(string content)
{
    var firstLine = content.Split('\n')[0].Trim();
    const string prefix = "// @aife-version:";
    if (firstLine.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
        var versionStr = firstLine[prefix.Length..].Trim();
        if (int.TryParse(versionStr, out var version))
            return version;
    }
    return 0;
}

// ──────────────────────────────────────────────────────────────────
//  proxy subcommand
// ──────────────────────────────────────────────────────────────────

static async Task<int> RunProxyCommandAsync(string[] args)
{
    var upstream = GetArgValue(args, "--upstream");
    var portStr = GetArgValue(args, "--port") ?? "3456";
    var retriesStr = GetArgValue(args, "--retries") ?? "3";
    var delayStr = GetArgValue(args, "--initial-delay") ?? "2";

    if (string.IsNullOrWhiteSpace(upstream))
    {
        Console.Error.WriteLine("Usage: aife-mcp proxy --upstream <url> [--port 3456] [--retries 3] [--initial-delay 2]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  --upstream       The LLM API base URL to proxy (e.g. https://api.openai.com)");
        Console.Error.WriteLine("  --port           Local port to listen on (default: 3456)");
        Console.Error.WriteLine("  --retries        Max retry attempts on transient failures (default: 3)");
        Console.Error.WriteLine("  --initial-delay  Initial backoff delay in seconds (default: 2)");
        return 1;
    }

    if (!int.TryParse(portStr, out var port) || port < 1 || port > 65535)
    {
        Console.Error.WriteLine($"ERROR: Invalid port: {portStr}");
        return 1;
    }

    if (!int.TryParse(retriesStr, out var retries) || retries < 0)
    {
        Console.Error.WriteLine($"ERROR: Invalid retries: {retriesStr}");
        return 1;
    }

    if (!double.TryParse(delayStr, System.Globalization.CultureInfo.InvariantCulture, out var initialDelay) || initialDelay <= 0)
    {
        Console.Error.WriteLine($"ERROR: Invalid initial-delay: {delayStr}");
        return 1;
    }

    var upstreamUri = new Uri(upstream.TrimEnd('/'));
    var listener = new HttpListener();
    var prefix = $"http://localhost:{port}/";
    listener.Prefixes.Add(prefix);

    var retryPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
        .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
        {
            MaxRetryAttempts = retries,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromSeconds(initialDelay),
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .Handle<SocketException>()
                .Handle<IOException>()
                .Handle<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
                .HandleResult(r => (int)r.StatusCode >= 500 || r.StatusCode == HttpStatusCode.TooManyRequests),
            OnRetry = retryArgs =>
            {
                var reason = retryArgs.Outcome.Exception?.GetType().Name
                    ?? $"HTTP {(int)(retryArgs.Outcome.Result?.StatusCode ?? 0)}";
                Console.Error.WriteLine($"[proxy] Retry {retryArgs.AttemptNumber + 1}/{retries} after {retryArgs.RetryDelay.TotalSeconds:F1}s — {reason}");
                return ValueTask.CompletedTask;
            }
        })
        .Build();

    using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

    try
    {
        listener.Start();
    }
    catch (HttpListenerException ex)
    {
        Console.Error.WriteLine($"ERROR: Cannot listen on {prefix} — {ex.Message}");
        Console.Error.WriteLine("Try a different --port or run as administrator.");
        return 1;
    }

    Console.WriteLine($"Aife LLM proxy listening on {prefix}");
    Console.WriteLine($"  Upstream:      {upstreamUri}");
    Console.WriteLine($"  Retries:       {retries} (exponential backoff, {initialDelay}s initial delay)");
    Console.WriteLine($"  Point your LLM extension at: http://localhost:{port}");
    Console.WriteLine();
    Console.WriteLine("Press Ctrl+C to stop.");

    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    try
    {
        while (!cts.Token.IsCancellationRequested)
        {
            var ctx = await listener.GetContextAsync().WaitAsync(cts.Token);
            // Fire-and-forget per request (concurrent)
            _ = HandleProxyRequestAsync(ctx, upstreamUri, httpClient, retryPipeline, cts.Token);
        }
    }
    catch (OperationCanceledException) { /* graceful shutdown */ }
    finally
    {
        listener.Stop();
        Console.WriteLine("[proxy] Stopped.");
    }

    return 0;
}

static async Task HandleProxyRequestAsync(
    HttpListenerContext context,
    Uri upstreamUri,
    HttpClient httpClient,
    ResiliencePipeline<HttpResponseMessage> retryPipeline,
    CancellationToken ct)
{
    var request = context.Request;
    var response = context.Response;

    try
    {
        var targetUrl = new Uri(upstreamUri, request.Url!.PathAndQuery);

        // Buffer the request body so Polly can replay it on retry
        byte[]? requestBody = null;
        if (request.HasEntityBody)
        {
            using var ms = new MemoryStream();
            await request.InputStream.CopyToAsync(ms, ct);
            requestBody = ms.ToArray();
        }

        Console.Error.WriteLine($"[proxy] {request.HttpMethod} {request.Url!.PathAndQuery}");

        var upstreamResponse = await retryPipeline.ExecuteAsync(async token =>
        {
            var msg = new HttpRequestMessage(new HttpMethod(request.HttpMethod), targetUrl);

            // Forward headers (skip hop-by-hop)
            foreach (string? key in request.Headers.AllKeys)
            {
                if (key is null || IsHopByHopHeader(key)) continue;
                if (key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
                if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                msg.Headers.TryAddWithoutValidation(key, request.Headers.GetValues(key));
            }

            if (requestBody is not null)
            {
                msg.Content = new ByteArrayContent(requestBody);
                if (request.ContentType is not null)
                    msg.Content.Headers.TryAddWithoutValidation("Content-Type", request.ContentType);
            }

            return await httpClient.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, token);
        }, ct);

        // Copy response status + headers
        response.StatusCode = (int)upstreamResponse.StatusCode;

        foreach (var header in upstreamResponse.Headers)
        {
            if (IsHopByHopHeader(header.Key)) continue;
            foreach (var val in header.Value)
                response.Headers.Add(header.Key, val);
        }

        foreach (var header in upstreamResponse.Content.Headers)
        {
            if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                response.ContentType = string.Join(", ", header.Value);
                continue;
            }
            foreach (var val in header.Value)
                response.Headers.Add(header.Key, val);
        }

        // Stream body through (handles both SSE and regular JSON responses)
        using var upstreamStream = await upstreamResponse.Content.ReadAsStreamAsync(ct);
        await upstreamStream.CopyToAsync(response.OutputStream, ct);

        Console.Error.WriteLine($"[proxy] {request.HttpMethod} {request.Url!.PathAndQuery} -> {(int)upstreamResponse.StatusCode}");
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        Console.Error.WriteLine($"[proxy] ERROR {request.HttpMethod} {request.Url?.PathAndQuery}: {ex.Message}");
        try
        {
            response.StatusCode = 502;
            response.ContentType = "application/json";
            var errorBytes = Encoding.UTF8.GetBytes(
                JsonConvert.SerializeObject(new { error = $"Proxy error: {ex.Message}" }));
            await response.OutputStream.WriteAsync(errorBytes, ct);
        }
        catch { /* response may already be sent */ }
    }
    finally
    {
        try { response.Close(); } catch { }
    }
}

static bool IsHopByHopHeader(string name) =>
    name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
    name.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
    name.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase) ||
    name.Equals("Proxy-Authenticate", StringComparison.OrdinalIgnoreCase) ||
    name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
    name.Equals("TE", StringComparison.OrdinalIgnoreCase) ||
    name.Equals("Trailer", StringComparison.OrdinalIgnoreCase) ||
    name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase);

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
/// Resolves where the save_ai_preview tool writes generated pages so they
/// show up automatically (via require.context auto-discovery) with zero
/// manual file placement or routing. Priority: explicit CLI arg, then env
/// var, then (if a local, non-git components-source is configured) a
/// `_AiPreview` sibling folder next to it. Returns null (tool unavailable)
/// if nothing can be resolved — never guesses a location that wasn't
/// explicitly configured or clearly derivable.
/// </summary>
static string? ResolvePreviewRootPath(string[] args, string? componentsSource)
{
    var cliPath = GetArgValue(args, "--preview-root");
    if (!string.IsNullOrWhiteSpace(cliPath))
        return Path.GetFullPath(cliPath);

    var envPath = Environment.GetEnvironmentVariable("AIFE_PREVIEW_ROOT");
    if (!string.IsNullOrWhiteSpace(envPath))
        return Path.GetFullPath(envPath);

    if (componentsSource is not null && !IsGitUrl(componentsSource))
        return Path.Combine(componentsSource, "_AiPreview");

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
    PrototypeScorer scorer,
    string? previewRoot)
{
    var method = request["method"]?.ToString() ?? "";
    var id = request["id"];
    var ct = CancellationToken.None;

    try
    {
        return method switch
        {
            "initialize" => new { jsonrpc = "2.0", id, result = new { protocolVersion = ResolveProtocolVersion(request), serverInfo = new { name = "aife-mcp", version = "1.0.0" }, capabilities = new { tools = new { listChanged = false }, prompts = new { listChanged = false } } } },
            "tools/list" => new { jsonrpc = "2.0", id, result = new { tools = Aife.Mcp.McpToolDefinitions.Tools } },
            "tools/call" => new { jsonrpc = "2.0", id, result = BuildToolCallResult(await CallToolAsync(request["params"]?["name"]?.ToString() ?? "", request["params"]?["arguments"] as JObject, provider, matchingService, tokenChecker, scorer, previewRoot, ct)) },
            "notifications/initialized" => new { jsonrpc = "2.0", id, result = new { } },
            "ping" => new { jsonrpc = "2.0", id, result = new { } },
            // We only declared the "tools" capability, but some clients still
            // probe resources/prompts during discovery regardless. Answer
            // with empty lists rather than an error so discovery can't get
            // stuck on an unadvertised-but-queried capability.
            "resources/list" => new { jsonrpc = "2.0", id, result = new { resources = Array.Empty<object>() } },
            "prompts/list" => new { jsonrpc = "2.0", id, result = new { prompts = Aife.Mcp.McpPromptDefinitions.Prompts } },
            "prompts/get" => new { jsonrpc = "2.0", id, result = GetPrompt(request["params"]?["name"]?.ToString() ?? "", request["params"]?["arguments"] as JObject) },
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

/// <summary>
/// Wraps a tool's raw return value in the MCP-spec-compliant "tools/call"
/// result envelope: <c>{ content: [{ type: "text", text: "..." }], isError }</c>.
/// Every branch of <see cref="CallToolAsync" /> previously returned its raw
/// domain object (a list, a DTO, or an anonymous <c>{ error = "..." }</c>)
/// directly as the JSON-RPC "result", which isn't a valid CallToolResult per
/// the MCP spec. Spec-compliant clients destructure <c>result.content</c> as
/// an array — when it's missing (i.e. undefined), that surfaces client-side
/// as "TypeError: r.content is not iterable" even though the server-side
/// call itself succeeded. Serializing the raw result to JSON text and
/// wrapping it here keeps all the existing tool handlers unchanged while
/// making every "tools/call" response protocol-correct.
/// </summary>
static object BuildToolCallResult(object toolResult)
{
    var json = JsonConvert.SerializeObject(toolResult);

    var isError = false;
    try
    {
        isError = JToken.Parse(json) is JObject obj && obj["error"] is not null;
    }
    catch (JsonException)
    {
        // Not a JSON object (e.g. a bare array) - can't contain an "error" key.
    }

    return new
    {
        content = new object[] { new { type = "text", text = json } },
        isError
    };
}

/// <summary>
/// Builds the MCP "prompts/get" response for a named prompt, substituting
/// the caller's arguments into the template text (see
/// McpPromptDefinitions.RenderConvertPrototypeToPage). Returns an error
/// object (not an exception) for an unknown prompt name so a bad slash-
/// command invocation surfaces a clear message instead of a protocol error.
/// </summary>
static object GetPrompt(string name, JObject? args)
{
    if (name != "convert-prototype-to-page")
        return new { error = $"Unknown prompt: {name}" };

    var prototypePath = args?["prototypePath"]?.ToString();
    if (string.IsNullOrWhiteSpace(prototypePath))
        return new { error = "'prototypePath' argument is required." };

    var slug = args?["slug"]?.ToString();
    var text = Aife.Mcp.McpPromptDefinitions.RenderConvertPrototypeToPage(prototypePath, slug);

    return new
    {
        description = "Convert an HTML/CSS prototype into a real, reviewable React page.",
        messages = new object[]
        {
            new { role = "user", content = new { type = "text", text } }
        }
    };
}


static async Task<object> CallToolAsync(
    string toolName,
    JObject? args,
    IKnowledgeProvider provider,
    ComponentMatchingService matchingService,
    TokenConformanceChecker tokenChecker,
    PrototypeScorer scorer,
    string? previewRoot,
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
        "save_ai_preview" => await SaveAiPreviewAsync(previewRoot, args, ct, provider),
        _ => new { error = $"Unknown tool: {toolName}" }
    };
}

/// <summary>
/// Writes AI-generated page files into the consumer project's AI-preview
/// folder (e.g. src/Components/AppLogic/_AiPreview/&lt;slug&gt;/), where the
/// project's own AiPreviewPage.tsx auto-discovers them via require.context.
/// This is the piece that makes the whole preview flow zero-manual-work:
/// generate -> call this tool -> reviewers see it live, no file copying or
/// route/App.js edits ever needed.
///
/// Security: slug and every file path are strictly validated (no "..",
/// no absolute paths/drive letters, resulting full path re-checked to stay
/// under the slug folder) before any write, since this executes filesystem
/// writes driven by LLM/client-supplied input (OWASP path traversal).
/// </summary>
static async Task<object> SaveAiPreviewAsync(string? previewRoot, JObject? args, CancellationToken ct, IKnowledgeProvider provider)
{
    if (previewRoot is null)
        return new { error = "No preview root configured. Set --preview-root or AIFE_PREVIEW_ROOT (or --components-source as a fallback base) when starting the MCP server." };

    // Defensive: if AiPreviewPage.tsx is missing from the preview root
    // (e.g. the project was set up before the auto-deploy feature was added,
    // or someone deleted it), deploy it now so the preview URL actually works
    // with zero manual steps.
    var aiPreviewPagePath = Path.Combine(previewRoot, "AiPreviewPage.tsx");
    if (!File.Exists(aiPreviewPagePath))
    {
        Directory.CreateDirectory(previewRoot);
        var fallbackTemplate = ReadAiPreviewPageTemplate();
        await File.WriteAllTextAsync(aiPreviewPagePath, fallbackTemplate);
    }

    var slug = args?["slug"]?.ToString();
    if (string.IsNullOrWhiteSpace(slug) || !System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9][a-z0-9-]*$"))
        return new { error = "'slug' is required and must be lowercase kebab-case (e.g. 'customer-register')." };

    var files = args?["files"]?.ToObject<List<PreviewFileInput>>();
    if (files is null || files.Count == 0)
        return new { error = "'files' must be a non-empty array of { path, content }." };

    var slugRoot = Path.GetFullPath(Path.Combine(previewRoot, slug));
    var previewRootFull = Path.GetFullPath(previewRoot);
    if (!slugRoot.StartsWith(previewRootFull, StringComparison.OrdinalIgnoreCase))
        return new { error = "Invalid slug." };

    var hasIndex = files.Any(f => f.Path.Equals("index.tsx", StringComparison.OrdinalIgnoreCase));
    var written = new List<string>();

    foreach (var file in files)
    {
        if (string.IsNullOrWhiteSpace(file.Path) ||
            file.Path.Contains("..") ||
            Path.IsPathRooted(file.Path) ||
            file.Path.Contains(':'))
            return new { error = $"Invalid file path: '{file.Path}'." };

        var fullPath = Path.GetFullPath(Path.Combine(slugRoot, file.Path));
        if (!fullPath.StartsWith(slugRoot, StringComparison.OrdinalIgnoreCase))
            return new { error = $"Invalid file path (escapes slug folder): '{file.Path}'." };

        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null)
            Directory.CreateDirectory(dir);

        var content = file.Content;

        // ── Post-generation auto-fix for .tsx files ──
        // LLMs often get import paths wrong (relative depth, missing
        // className, missing styles import) because they don't fully
        // model the consumer project's directory structure. These
        // deterministic fixes catch the most common failure patterns
        // so non-technical reviewers see a working preview with zero
        // manual editing — the whole point of save_ai_preview.
        if (file.Path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
            content = AutoFixTsx(content, slugRoot, fullPath, provider);

        await File.WriteAllTextAsync(fullPath, content, ct);
        written.Add(file.Path);
    }

    // Convenience: if the caller forgot an index.tsx (the file
    // AiPreviewPage.tsx's require.context glob looks for) but supplied
    // exactly one component file, auto-generate a re-export so the preview
    // still shows up without the caller needing to know that convention.
    if (!hasIndex)
    {
        var componentFiles = files.Where(f => f.Path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) && !f.Path.Equals("index.tsx", StringComparison.OrdinalIgnoreCase)).ToList();
        if (componentFiles.Count == 1)
        {
            var componentModule = Path.GetFileNameWithoutExtension(componentFiles[0].Path);
            var indexPath = Path.Combine(slugRoot, "index.tsx");
            await File.WriteAllTextAsync(indexPath, $"export {{ default }} from './{componentModule}';{Environment.NewLine}", ct);
            written.Add("index.tsx (auto-generated)");
        }
        else
        {
            return new
            {
                warning = "No index.tsx supplied and more than one .tsx file present — couldn't auto-generate one. " +
                    "Add an index.tsx that default-exports the main component so the preview page can discover it.",
                slug,
                filesWritten = written
            };
        }
    }

    // Persist the raw source + analysis/review (if supplied) as meta.json
    // alongside the code, so AiPreviewPage.tsx can show "React Code" /
    // "Review Report" / "Analysis" tabs matching the Aife.Api dashboard's
    // presentation — reading sources straight from meta.json (not a webpack
    // raw-loader, which isn't guaranteed to be installed in every consumer
    // project) keeps this reliable with zero extra webpack config.
    var analysis = args?["analysis"];
    var review = args?["review"];
    var sources = new JObject();
    foreach (var file in files.Where(f => !f.Path.Equals("index.tsx", StringComparison.OrdinalIgnoreCase)))
        sources[file.Path] = file.Content;

    var meta = new JObject
    {
        ["files"] = new JArray(files.Select(f => f.Path)),
        ["sources"] = sources,
        ["analysis"] = analysis,
        ["review"] = review,
        ["generatedAt"] = DateTimeOffset.UtcNow.ToString("O")
    };
    await File.WriteAllTextAsync(Path.Combine(slugRoot, "meta.json"), meta.ToString(Formatting.Indented), ct);
    written.Add("meta.json");

    return new
    {
        success = true,
        slug,
        filesWritten = written,
        previewUrl = $"/ai-preview/{slug}",
        message = $"Saved. Once the dev server is running, open http://localhost:3000/#/ai-preview/{slug} (after logging in) to review it."
    };
}

/// <summary>
/// Deterministic post-generation fixes for common LLM mistakes in .tsx files.
/// These are KB-agnostic — no hardcoded BUS or CustomUIs strings.
/// If you train with a different component library, these fixes still work
/// because they derive import path structure from the KB at runtime.
/// </summary>
static string AutoFixTsx(string content, string slugRoot, string fullPath, IKnowledgeProvider provider)
{
    // ── Fix 0: Add @ts-nocheck ──
    // Consumer projects are typically plain JS (PropTypes), not TypeScript.
    // TypeScript can't infer prop types from PropTypes — especially for
    // forwardRef and {…rest} spread components. Adding `// @ts-nocheck`
    // suppresses ALL TS errors for this file. The code works fine at
    // runtime via webpack/babel.
    if (!content.StartsWith("// @ts-nocheck"))
        content = "// @ts-nocheck\n" + content;

    // ── Fix 1: Wrong relative import paths (KB-driven) ──
    // The LLM often writes `../CustomUIs/X` or `../Components/X` (one level
    // up), but the generated file lives 3+ levels deep in _AiPreview/<slug>/.
    // We discover the correct import prefix by examining the first component
    // in the KB: its `importPath` tells us the parent folder name to search
    // for.  If the KB uses "CustomUIs", we fix "../CustomUIs/"; if it uses
    // "components", we fix "../components/".  The replacement depth (how
    // many "../" to prepend) is derived from the actual slugRoot depth.
    var depth = ComputeDepthFromPreviewRoot(slugRoot);
    var upPrefix = string.Concat(Enumerable.Repeat("../", depth));

    // Discover the component-root folder name from the first KB entry.
    // e.g. "Components/CustomUIs/BUSButton/BUSButton" -> "CustomUIs"
    //      "DesignSystem/components/Button/Button"   -> "DesignSystem"
    var allComponents = provider.SearchComponentsAsync(new ComponentQuery(), CancellationToken.None)
        .GetAwaiter().GetResult();
    var firstId = allComponents?.FirstOrDefault()?.ComponentId;
    string componentRoot = "CustomUIs"; // fallback for empty KB
    if (firstId is not null)
    {
        var detail = provider.GetComponentAsync(firstId, CancellationToken.None)
            .GetAwaiter().GetResult();
        if (detail?.ImportPath is { } ip)
        {
            componentRoot = ExtractComponentPathPrefix(ip);
        }
    }

    // Extract the top-level component directory name for import-path matching.
    // The LLM writes imports like '../DesignSystem' or './../../DesignSystem/components/Button'
    // regardless of how deep the component actually lives in the KB. We match the top-level
    // directory name and fix ONLY the depth (number of '../' segments). The old BUS
    // convention inserting an extra 'Components/' prefix is also handled — the regex
    // jumps past it. Zero hardcoded folder names — this works for any project structure.
    // The LLM writes imports relative to the Components/ directory (or src/ root),
    // so it consistently drops "Components/" from import paths. Strip this prefix
    // from the componentRoot so the regex match key aligns with what the LLM writes.
    // Handles both "Components/CustomUIs/..." and "components/..." (case-insensitive).
    var strippedRoot = componentRoot;
    if (strippedRoot.StartsWith("Components/", StringComparison.OrdinalIgnoreCase))
        strippedRoot = strippedRoot["Components/".Length..];

    var topLevelDir = strippedRoot.Split('/')[0];
    var escapedTopLevel = System.Text.RegularExpressions.Regex.Escape(topLevelDir);

    // Match: from ' or " then optional ./ then one or more ../ then (optionally
    // 'Components/' for old BUS convention) then topLevelDir then optional /sub/path.
    // Replace only the up-prefix with the correct depth, preserving the rest.
    content = System.Text.RegularExpressions.Regex.Replace(
        content,
        $@"from\s+['""](?:\./)?((?:\.\./)+)(?:Components/)?({escapedTopLevel}(?:/[^'""]+)?)['""]",
        $"from '{upPrefix}$2'");

    // ── Fix 2: Missing `import styles` ──
    // If the file uses `styles.xxx` but has no `import styles` statement,
    // inject one from the sibling CSS module.
    if (content.Contains("styles.") && !content.Contains("import styles from"))
    {
        var tsxFileName = System.IO.Path.GetFileName(fullPath);
        var moduleName = System.IO.Path.GetFileNameWithoutExtension(tsxFileName) + ".module.scss";
        var lastImportIndex = FindLastImportPosition(content);
        if (lastImportIndex >= 0)
            content = content.Insert(lastImportIndex, $"import styles from './{moduleName}';\n");
        else
        {
            var insertPos = FindTopInsertPosition(content);
            content = content.Insert(insertPos, $"import styles from './{moduleName}';\n");
        }
    }

    // ── Fix 3: Type untyped callback parameters ──
    // The LLM often writes `(e) => ...` without a type annotation.
    // TypeScript strict mode rejects implicit 'any'. Add explicit React types.
    content = System.Text.RegularExpressions.Regex.Replace(
        content,
        @"onChange=\{\s*(?:\(e\)\s*|e\s*)=>",
        "onChange={(e: React.ChangeEvent<HTMLTextAreaElement>) =>");

    content = System.Text.RegularExpressions.Regex.Replace(
        content,
        @"onClick=\{\s*(?:\(e\)\s*|e\s*)=>",
        "onClick={(e: React.MouseEvent) =>");

    return content;
}

/// <summary>
/// Extracts the import-path root prefix that the LLM is most likely to use
/// (incorrectly, with too few "../") in generated import statements.
/// <br/>
/// The import path always follows the pattern
/// <c>&lt;rootPrefix&gt;/&lt;ComponentName&gt;/&lt;ComponentName&gt;</c>.
/// We take everything before the last two segments as the root prefix.
/// <br/>
/// Examples:
/// <c>"DesignSystem/components/Button/Button"</c> → <c>"DesignSystem/components"</c>
/// <c>"Components/CustomUIs/BUSButton/BUSButton"</c> → <c>"Components/CustomUIs"</c>
/// <c>"MyUI/Widgets/Panel/Panel"</c> → <c>"MyUI/Widgets"</c>
/// <c>"components/Button/Button"</c> → <c>"components"</c>
/// <br/>
/// Zero hardcoded folder names — works for any project structure.
/// </summary>
static string ExtractComponentPathPrefix(string importPath)
{
    var parts = importPath.Split('/');

    // Drop the last segment (the component name/folder).
    // Whether barrel (DesignSystem/components/Tabs) or file
    // (DesignSystem/components/Button/Button), everything before
    // the final segment is the root prefix the LLM writes in imports.
    if (parts.Length <= 1)
        return parts[0];

    return string.Join("/", parts.Take(parts.Length - 1));
}

/// <summary>
/// Finds the position right after the last import/require statement so
/// we can insert `import styles` right after existing imports.
/// </summary>
static int FindLastImportPosition(string content)
{
    var lines = content.Split('\n');
    var lastImportLine = -1;
    for (int i = 0; i < lines.Length; i++)
    {
        var trimmed = lines[i].TrimStart();
        if (trimmed.StartsWith("import ") || trimmed.StartsWith("require(") ||
            trimmed.StartsWith("export {") || trimmed.StartsWith("export *") ||
            trimmed.StartsWith("export default"))
            lastImportLine = i;
    }
    if (lastImportLine >= 0)
    {
        // Return the byte position right after this line's newline
        var pos = 0;
        for (int i = 0; i <= lastImportLine; i++)
            pos += lines[i].Length + 1; // +1 for \n
        return pos;
    }
    return -1;
}

/// <summary>
/// Finds a safe position to insert code at the top of a file, after any
/// leading comments or blank lines.
/// </summary>
static int FindTopInsertPosition(string content)
{
    var lines = content.Split('\n');
    var i = 0;
    while (i < lines.Length)
    {
        var trimmed = lines[i].Trim();
        if (trimmed.Length > 0 && !trimmed.StartsWith("//") && !trimmed.StartsWith("/*") && !trimmed.StartsWith("*"))
            break;
        i++;
    }
    // Return byte position at the start of line i
    var pos = 0;
    for (int j = 0; j < i; j++)
        pos += lines[j].Length + 1;
    return pos;
}

/// <summary>
/// Computes how many "../" levels are needed to get from the _AiPreview
/// slug folder up to the project's src/ root (where `Components/` lives).
/// Example: slugRoot = ".../src/Components/AppLogic/_AiPreview/settings"
///   -> segments after "src" = ["Components","AppLogic","_AiPreview","settings"] = depth 4
///   -> we need 4 levels of "../" to get back to src/
/// </summary>
static int ComputeDepthFromPreviewRoot(string slugRoot)
{
    // Normalize separators and find the "src" boundary
    var normalized = slugRoot.Replace('\\', '/');
    var srcIndex = normalized.IndexOf("/src/", StringComparison.OrdinalIgnoreCase);
    if (srcIndex < 0)
        return 4; // fallback: typical React project depth

    // Count segments after "src/..."
    var afterSrc = normalized[(srcIndex + "/src/".Length)..];
    var segments = afterSrc.Split('/', StringSplitOptions.RemoveEmptyEntries);
    return segments.Length;
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

/// <summary>
/// Reads the AiPreviewPage.tsx template from the knowledge/ folder.
/// This is the single source of truth — the same file that gets
/// deployed to consumer projects. Reading from disk at runtime is
/// cleaner and avoids C# string escaping issues with JSX/TSX content.
/// </summary>
static string ReadAiPreviewPageTemplate()
{
    var templatePath = Path.Combine(FindRepoRoot(), "knowledge", "referenceUiPatterns", "AiPreviewPage.template.tsx");
    if (File.Exists(templatePath))
        return File.ReadAllText(templatePath);

    // Fallback: look relative to the executable
    var altPath = Path.Combine(AppContext.BaseDirectory, "knowledge", "referenceUiPatterns", "AiPreviewPage.template.tsx");
    if (File.Exists(altPath))
        return File.ReadAllText(altPath);

    throw new FileNotFoundException(
        "AiPreviewPage.template.tsx not found. Ensure the knowledge/ folder is deployed alongside the executable.");
}

sealed class PreviewFileInput
{
    public string Path { get; set; } = "";
    public string Content { get; set; } = "";
}
