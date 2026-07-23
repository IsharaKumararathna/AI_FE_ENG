using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Aife.Knowledge;

public sealed record ConformanceViolationCluster
{
    public required string RuleId { get; init; }
    public required string Category { get; init; }
    public int Count { get; init; }
    public required IReadOnlyList<string> DistinctValues { get; init; }
    public string? SampleMessage { get; init; }
}

public sealed record ConformanceSummary
{
    public int TotalReports { get; init; }
    public int ReportsWithFindings { get; init; }
    public int ReportsClean { get; init; }
    public int TotalFindings { get; init; }
    public required IReadOnlyList<ConformanceViolationCluster> TopViolations { get; init; }
}

/// <summary>
/// Closes the conformance feedback loop. Reads every prototype conformance
/// report under a directory (e.g. <c>data/conformance/</c>), clusters recurring
/// violations by <c>ruleId</c> (+ normalized value, e.g. the offending hex
/// literal for color-token violations), and emits a summary that drives
/// prompt/KB improvements — turning the 70+ open-loop artifacts into actionable
/// training signal. The recurring distinct hex list feeds the banned-literal
/// section of the <c>react.generator</c> prompt.
/// </summary>
public sealed class ConformanceAggregator
{
    private static readonly Regex HexColorRegex = new(@"#[0-9a-fA-F]{3,8}", RegexOptions.Compiled);

    private readonly string _conformanceDir;

    public ConformanceAggregator(string conformanceDir)
    {
        _conformanceDir = conformanceDir;
    }

    public ConformanceSummary Aggregate()
    {
        if (!Directory.Exists(_conformanceDir))
            return new ConformanceSummary { TopViolations = Array.Empty<ConformanceViolationCluster>() };

        var reports = Directory.GetFiles(_conformanceDir, "*.json");
        var withFindings = 0;
        var clean = 0;
        var totalFindings = 0;
        var clusters = new Dictionary<string, ClusterBuilder>(StringComparer.Ordinal);

        foreach (var file in reports)
        {
            try
            {
                var doc = JObject.Parse(File.ReadAllText(file));
                var findings = doc["findings"] as JArray;
                var count = findings?.Count ?? 0;
                if (count > 0) withFindings++; else clean++;

                if (findings is null) continue;
                foreach (var f in findings)
                {
                    totalFindings++;
                    var ruleId = f["ruleId"]?.ToString() ?? "UNKNOWN";
                    var category = f["category"]?.ToString() ?? "Unknown";
                    var message = f["message"]?.ToString() ?? "";
                    if (!clusters.TryGetValue(ruleId, out var b))
                    {
                        b = new ClusterBuilder(ruleId, category);
                        clusters[ruleId] = b;
                    }
                    b.Add(message, ExtractValue(ruleId, message));
                }
            }
            catch { /* skip malformed report */ }
        }

        var top = clusters.Values
            .OrderByDescending(c => c.Count)
            .Take(20)
            .Select(c => c.Build())
            .ToList();

        return new ConformanceSummary
        {
            TotalReports = reports.Length,
            ReportsWithFindings = withFindings,
            ReportsClean = clean,
            TotalFindings = totalFindings,
            TopViolations = top
        };
    }

    /// <summary>Aggregates, writes the summary to <paramref name="outputPath"/>, and returns it.</summary>
    public ConformanceSummary WriteSummary(string outputPath)
    {
        var summary = Aggregate();
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var json = JsonConvert.SerializeObject(summary, new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        });
        File.WriteAllText(outputPath, json);
        return summary;
    }

    /// <summary>
    /// For color/token violations, cluster on the offending hex literal so the
    /// summary lists exactly which colors keep getting hardcoded. Returns null
    /// for violations without an extractable value.
    /// </summary>
    private static string? ExtractValue(string ruleId, string message)
    {
        if (ruleId.IndexOf("COLOR", StringComparison.OrdinalIgnoreCase) >= 0
            || ruleId.IndexOf("TOKEN", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var m = HexColorRegex.Match(message);
            if (m.Success) return m.Value.ToLowerInvariant();
        }
        return null;
    }

    private sealed class ClusterBuilder
    {
        private readonly string _ruleId;
        private readonly string _category;
        private readonly HashSet<string> _values = new(StringComparer.Ordinal);
        private string? _sample;
        public int Count { get; private set; }

        public ClusterBuilder(string ruleId, string category)
        {
            _ruleId = ruleId;
            _category = category;
        }

        public void Add(string message, string? value)
        {
            Count++;
            if (value is not null) _values.Add(value);
            _sample ??= message;
        }

        public ConformanceViolationCluster Build() => new()
        {
            RuleId = _ruleId,
            Category = _category,
            Count = Count,
            DistinctValues = _values.OrderBy(v => v).ToList(),
            SampleMessage = _sample
        };
    }
}
