using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Aife.Application.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Polly;

namespace Aife.Infrastructure.AI;

/// <summary>
/// An OpenAI-compatible LLM provider that works with any API that follows the
/// OpenAI chat completions contract (DeepSeek, GLM/Novita, OpenAI, Azure OpenAI
/// with Entra ID, etc.).
///
/// Configure in appsettings.json or environment variables:
///   LLM__Endpoint=https://api.deepseek.com/v1  (or https://api.novita.ai/v3/openai)
///   LLM__ApiKey=sk-xxx
///   LLM__Model=deepseek-chat                  (or glm-4)
///   LLM__Name=DeepSeek
///   LLM__Priority=10
/// </summary>
public sealed class OpenAiCompatibleProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _model;

    public LlmProviderInfo Info { get; }

    public OpenAiCompatibleProvider(HttpClient httpClient, string name, string model, int priority = 10)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _model = model ?? throw new ArgumentNullException(nameof(model));

        Info = new LlmProviderInfo
        {
            Name = name,
            Priority = priority,
            Capabilities = new List<string> { "text", "json" }
        };
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var payload = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = request.SystemMessage },
                new { role = "user", content = request.UserMessage }
            },
            temperature = 0.1,
            max_tokens = 8192
        };

        var json = JsonConvert.SerializeObject(payload, new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        });

        // Retry policy: handles ECONNRESET, timeouts, and transient 5xx errors
        // with exponential backoff. Runs automatically — zero config for end users.
        var retryPolicy = Policy
            .Handle<HttpRequestException>(ex => IsTransient(ex))
            .Or<TaskCanceledException>(ex => !ct.IsCancellationRequested) // timeout, not user cancel
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), // 2s, 4s, 8s
                onRetry: (exception, delay, attempt, _) =>
                {
                    Console.Error.WriteLine(
                        $"[LLM Retry] {Info.Name}: attempt {attempt}/3 after {exception.GetType().Name}" +
                        $" — retrying in {delay.TotalSeconds:F0}s ({exception.Message})");
                });

        return await retryPolicy.ExecuteAsync(async (pollyCtx) =>
        {
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            var httpResponse = await _httpClient.SendAsync(httpRequest, ct);

            var responseJson = await httpResponse.Content.ReadAsStringAsync(ct);

            // Throw on transient server errors so Polly can retry them
            if ((int)httpResponse.StatusCode >= 500)
            {
                throw new HttpRequestException(
                    $"LLM '{Info.Name}' {(int)httpResponse.StatusCode}. Body: {responseJson}",
                    null,
                    httpResponse.StatusCode);
            }

            if (!httpResponse.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"LLM '{Info.Name}' {(int)httpResponse.StatusCode}. Body: {responseJson}");
            }

            var response = JObject.Parse(responseJson);

            var text = response["choices"]?[0]?["message"]?["content"]?.ToString() ?? "";
            var usage = response["usage"];
            var finishReason = response["choices"]?[0]?["finish_reason"]?.ToString();

            text = ExtractJson(text, request.JsonMode);

            // Newtonsoft.Json cannot parse Infinity/NaN — replace with large numbers
            text = text.Replace(": Infinity", ": 1e10").Replace(": -Infinity", ": -1e10").Replace(": NaN", ": null");

            return new LlmResponse
            {
                Text = text,
                Usage = usage is not null
                    ? new LlmUsage
                    {
                        PromptTokens = (int)(usage["prompt_tokens"] ?? 0),
                        CompletionTokens = (int)(usage["completion_tokens"] ?? 0),
                        TotalTokens = (int)(usage["total_tokens"] ?? 0)
                    }
                    : null,
                FinishReason = finishReason,
                ProviderName = Info.Name
            };
        }, new Context());
    }

    /// <summary>
    /// Determines whether an HttpRequestException represents a transient network
    /// failure that should be retried (ECONNRESET, ETIMEDOUT, ECONNREFUSED, 5xx).
    /// </summary>
    private static bool IsTransient(HttpRequestException ex)
    {
        // 5xx server errors
        if (ex.StatusCode.HasValue && (int)ex.StatusCode.Value >= 500)
            return true;

        // 429 Too Many Requests (rate limiting)
        if (ex.StatusCode is HttpStatusCode.TooManyRequests)
            return true;

        // Connection-level failures: ECONNRESET, ECONNREFUSED, ETIMEDOUT
        if (ex.InnerException is SocketException or IOException)
            return true;

        // Message-level hints (e.g. undici / curl error strings)
        var msg = ex.Message;
        if (msg.Contains("ECONNRESET", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("ETIMEDOUT", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("ECONNREFUSED", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("connection reset", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Strips markdown code fences and leading natural-language text from the
    /// LLM response. DeepSeek and similar models often prepend explanations
    /// before a JSON block.
    /// </summary>
    private static string ExtractJson(string text, bool jsonMode)
    {
        if (!jsonMode)
            return text;

        var trimmed = text.Trim();

        // Find the first JSON array or object at root level
        var jsonStart = -1;
        for (var i = 0; i < trimmed.Length; i++)
        {
            if (trimmed[i] == '[' || trimmed[i] == '{')
            {
                jsonStart = i;
                break;
            }
        }

        if (jsonStart > 0)
            trimmed = trimmed[jsonStart..];

        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            var start = trimmed.IndexOf('\n') + 1;
            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (end > start)
                return trimmed[start..end].Trim();
        }

        if (trimmed.StartsWith("```") && trimmed.EndsWith("```"))
        {
            var start = trimmed.IndexOf('\n') + 1;
            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (end > start)
                return trimmed[start..end].Trim();
        }

        return trimmed;
    }
}
