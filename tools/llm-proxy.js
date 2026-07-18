/**
 * LLM Retry Proxy — transparent retry layer for any OpenAI-compatible LLM API.
 *
 * Sits between a VS Code extension (or any HTTP client) and the upstream model API.
 * Transparently retries on transient network errors (ECONNRESET, ETIMEDOUT, ECONNREFUSED, socket hang up).
 * Works with any model provider: Zhipu GLM, Novita, OpenAI, Azure OpenAI, Anthropic, local Ollama, etc.
 *
 * Usage:
 *   node tools/llm-proxy.js
 *
 * Then point your extension's base URL to: http://localhost:3100
 *
 * Environment variables (all optional):
 *   LLM_PROXY_PORT      — Local port (default: 3100)
 *   LLM_UPSTREAM_URL    — Upstream base URL (default: https://open.bigmodel.cn/api/coding/paas/v4)
 *   LLM_MAX_RETRIES     — Max retry attempts (default: 3)
 *   LLM_INITIAL_DELAY   — Initial backoff in ms (default: 1000)
 */

const http = require("http");
const https = require("https");
const { URL } = require("url");

// --- Configuration ---
const PORT = parseInt(process.env.LLM_PROXY_PORT || "3100", 10);
const UPSTREAM = process.env.LLM_UPSTREAM_URL || "https://open.bigmodel.cn/api/coding/paas/v4";
const MAX_RETRIES = parseInt(process.env.LLM_MAX_RETRIES || "3", 10);
const INITIAL_DELAY_MS = parseInt(process.env.LLM_INITIAL_DELAY || "1000", 10);

// Errors worth retrying (transient network issues)
const RETRYABLE_CODES = new Set([
  "ECONNRESET",
  "ETIMEDOUT",
  "ECONNREFUSED",
  "EPIPE",
  "EHOSTUNREACH",
  "EAI_AGAIN",
  "UND_ERR_SOCKET",
]);

function isRetryable(err) {
  if (!err) return false;
  if (RETRYABLE_CODES.has(err.code)) return true;
  if (err.message && err.message.includes("socket hang up")) return true;
  if (err.message && err.message.includes("ECONNRESET")) return true;
  return false;
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function log(level, msg) {
  const ts = new Date().toISOString().slice(11, 23);
  process.stderr.write(`[${ts}] ${level}: ${msg}\n`);
}

/**
 * Forward a single request to upstream, returning the response or throwing on network error.
 */
function forwardRequest(method, path, headers, body) {
  return new Promise((resolve, reject) => {
    const upstream = new URL(path, UPSTREAM);
    const isHttps = upstream.protocol === "https:";
    const transport = isHttps ? https : http;

    // Clone headers, remove hop-by-hop, set correct host
    const fwdHeaders = { ...headers };
    delete fwdHeaders["host"];
    delete fwdHeaders["connection"];
    fwdHeaders["host"] = upstream.host;
    // Disable keep-alive to avoid stale pooled connections triggering ECONNRESET
    fwdHeaders["connection"] = "close";

    const options = {
      hostname: upstream.hostname,
      port: upstream.port || (isHttps ? 443 : 80),
      path: upstream.pathname + upstream.search,
      method,
      headers: fwdHeaders,
      // Fresh socket every time — no pooling
      agent: false,
      timeout: 120_000, // 2 min connect timeout
    };

    const req = transport.request(options, (res) => {
      resolve(res);
    });

    req.on("error", (err) => {
      reject(err);
    });

    req.on("timeout", () => {
      req.destroy(new Error("ETIMEDOUT"));
    });

    if (body && body.length > 0) {
      req.write(body);
    }
    req.end();
  });
}

/**
 * Handle an incoming request with retry logic.
 */
async function handleRequest(clientReq, clientRes) {
  const startTime = Date.now();

  // Buffer the request body (needed for retries)
  const chunks = [];
  for await (const chunk of clientReq) {
    chunks.push(chunk);
  }
  const body = Buffer.concat(chunks);

  const method = clientReq.method;
  const path = clientReq.url; // e.g. /v4/chat/completions or just /chat/completions

  log("INFO", `${method} ${path} (${body.length} bytes)`);

  let lastError = null;

  for (let attempt = 0; attempt <= MAX_RETRIES; attempt++) {
    if (attempt > 0) {
      const delay = INITIAL_DELAY_MS * Math.pow(2, attempt - 1);
      log("WARN", `Retry ${attempt}/${MAX_RETRIES} after ${delay}ms (${lastError?.code || lastError?.message})`);
      await sleep(delay);
    }

    try {
      const upstreamRes = await forwardRequest(method, path, clientReq.headers, body);

      // Success — pipe response back to client
      clientRes.writeHead(upstreamRes.statusCode, upstreamRes.headers);

      // For streaming: pipe and handle mid-stream errors
      await new Promise((resolve, reject) => {
        upstreamRes.on("error", (err) => {
          reject(err);
        });
        upstreamRes.pipe(clientRes);
        upstreamRes.on("end", resolve);
        clientRes.on("close", () => {
          // Client disconnected — abort upstream
          upstreamRes.destroy();
          resolve();
        });
      });

      const elapsed = Date.now() - startTime;
      log("INFO", `${method} ${path} -> ${upstreamRes.statusCode} (${elapsed}ms, attempt ${attempt + 1})`);
      return;
    } catch (err) {
      lastError = err;
      if (!isRetryable(err)) {
        // Non-retryable error — fail immediately
        log("ERROR", `Non-retryable error: ${err.code || err.message}`);
        break;
      }
      // If headers already sent (mid-stream failure), we can't retry
      if (clientRes.headersSent) {
        log("ERROR", `Mid-stream ${err.code} — headers already sent, cannot retry`);
        clientRes.destroy();
        return;
      }
    }
  }

  // All retries exhausted
  const elapsed = Date.now() - startTime;
  log("ERROR", `All ${MAX_RETRIES + 1} attempts failed after ${elapsed}ms: ${lastError?.code || lastError?.message}`);

  if (!clientRes.headersSent) {
    clientRes.writeHead(502, { "content-type": "application/json" });
    clientRes.end(
      JSON.stringify({
        error: {
          message: `Proxy: upstream unreachable after ${MAX_RETRIES + 1} attempts (${lastError?.code || lastError?.message})`,
          type: "proxy_error",
          code: lastError?.code || "UNKNOWN",
        },
      })
    );
  }
}

// --- Start server ---
const server = http.createServer((req, res) => {
  handleRequest(req, res).catch((err) => {
    log("ERROR", `Unhandled: ${err.message}`);
    if (!res.headersSent) {
      res.writeHead(500, { "content-type": "application/json" });
      res.end(JSON.stringify({ error: { message: "Internal proxy error" } }));
    }
  });
});

server.listen(PORT, "127.0.0.1", () => {
  log("INFO", `LLM Retry Proxy listening on http://127.0.0.1:${PORT}`);
  log("INFO", `Upstream: ${UPSTREAM}`);
  log("INFO", `Max retries: ${MAX_RETRIES}, initial delay: ${INITIAL_DELAY_MS}ms`);
  log("INFO", `Point your model extension base URL to: http://127.0.0.1:${PORT}`);
});

server.on("error", (err) => {
  if (err.code === "EADDRINUSE") {
    log("ERROR", `Port ${PORT} already in use. Set LLM_PROXY_PORT to use a different port.`);
    process.exit(1);
  }
  throw err;
});
