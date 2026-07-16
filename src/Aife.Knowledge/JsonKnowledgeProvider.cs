using Aife.Application.Knowledge;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Aife.Knowledge;

/// <summary>
/// MVP <see cref="IKnowledgeProvider" /> that reads the on-disk Knowledge Base
/// under a <c>knowledge/</c> directory (ADR-003). Loads the manifest, then loads
/// each referenced file into typed structures on first use, cached for the
/// process lifetime. Phase 2 swaps in <c>McpKnowledgeProvider</c> against the
/// same contract with no AI code change.
/// </summary>
public sealed class JsonKnowledgeProvider : IKnowledgeProvider
{
    private readonly string _knowledgePath;
    private readonly object _loadLock = new();
    private bool _loaded;

    private List<ComponentDetail> _components = new();
    private DesignTokenSet _tokens = new();
    private List<LayoutPattern> _layouts = new();
    private List<ReferenceUiPattern> _referenceUiPatterns = new();
    private List<Icon> _icons = new();
    private List<BestPractice> _bestPractices = new();
    private List<AccessibilityRule> _accessibilityRules = new();

    /// <param name="knowledgePath">Absolute path to the <c>knowledge/</c> directory.</param>
    public JsonKnowledgeProvider(string knowledgePath)
    {
        if (string.IsNullOrWhiteSpace(knowledgePath))
            throw new ArgumentException("Knowledge path must not be empty.", nameof(knowledgePath));

        _knowledgePath = knowledgePath;
    }

    /// <summary>
    /// Force reload from disk. Called after training to pick up new components.
    /// </summary>
    public void Reload()
    {
        lock (_loadLock)
        {
            _loaded = false;
            _components.Clear();
            _tokens = new();
            _layouts = new();
            _referenceUiPatterns = new();
            _icons = new();
            _bestPractices = new();
            _accessibilityRules = new();
        }
    }

    public Task<IReadOnlyList<ComponentSummary>> SearchComponentsAsync(ComponentQuery query, CancellationToken ct)
    {
        EnsureLoaded();
        var results = _components
            .Where(c => string.IsNullOrEmpty(query.Status) || c.Status.Equals(query.Status, StringComparison.OrdinalIgnoreCase))
            .Where(c => string.IsNullOrEmpty(query.Category) || c.Category.Equals(query.Category, StringComparison.OrdinalIgnoreCase))
            .Where(c => string.IsNullOrEmpty(query.Text) ||
                        c.Name.Contains(query.Text, StringComparison.OrdinalIgnoreCase) ||
                        (c.Description?.Contains(query.Text, StringComparison.OrdinalIgnoreCase) ?? false))
            .Select(c => new ComponentSummary
            {
                ComponentId = c.ComponentId,
                Name = c.Name,
                Category = c.Category,
                Status = c.Status
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<ComponentSummary>>(results);
    }

    public Task<ComponentDetail?> GetComponentAsync(string componentId, CancellationToken ct)
    {
        EnsureLoaded();
        var component = _components.FirstOrDefault(c =>
            c.ComponentId.Equals(componentId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(component);
    }

    public Task<ComponentProps?> GetComponentPropsAsync(string componentId, CancellationToken ct)
    {
        EnsureLoaded();
        var component = _components.FirstOrDefault(c =>
            c.ComponentId.Equals(componentId, StringComparison.OrdinalIgnoreCase));
        if (component is null)
            return Task.FromResult<ComponentProps?>(null);

        return Task.FromResult<ComponentProps?>(new ComponentProps
        {
            ComponentId = component.ComponentId,
            Props = component.Props
        });
    }

    public Task<IReadOnlyList<ComponentExample>> GetComponentExamplesAsync(string componentId, CancellationToken ct)
    {
        EnsureLoaded();
        var component = _components.FirstOrDefault(c =>
            c.ComponentId.Equals(componentId, StringComparison.OrdinalIgnoreCase));
        var examples = component?.Examples ?? new List<ComponentExample>();
        return Task.FromResult<IReadOnlyList<ComponentExample>>(examples.ToList());
    }

    public Task<IReadOnlyList<LayoutPattern>> GetLayoutPatternsAsync(CancellationToken ct)
    {
        EnsureLoaded();
        return Task.FromResult<IReadOnlyList<LayoutPattern>>(_layouts.ToList());
    }

    public Task<DesignTokenSet> GetDesignTokensAsync(CancellationToken ct)
    {
        EnsureLoaded();
        return Task.FromResult(_tokens);
    }

    public Task<IReadOnlyList<Icon>> GetIconsAsync(IconQuery query, CancellationToken ct)
    {
        EnsureLoaded();
        var results = _icons
            .Where(i => string.IsNullOrEmpty(query.Name) ||
                        i.Name.Contains(query.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult<IReadOnlyList<Icon>>(results);
    }

    public Task<IReadOnlyList<ComponentSummary>> SearchComponentsByDescriptionAsync(string description, CancellationToken ct)
    {
        EnsureLoaded();
        var results = _components
            .Where(c => c.Description?.Contains(description, StringComparison.OrdinalIgnoreCase) ?? false)
            .Select(c => new ComponentSummary
            {
                ComponentId = c.ComponentId,
                Name = c.Name,
                Category = c.Category,
                Status = c.Status
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<ComponentSummary>>(results);
    }

    public Task<IReadOnlyList<BestPractice>> GetBestPracticesAsync(BestPracticeQuery query, CancellationToken ct)
    {
        EnsureLoaded();
        var results = _bestPractices
            .Where(bp => query.Tags is null || query.Tags.Count == 0 ||
                         bp.Tags.Any(t => query.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)))
            .ToList();
        return Task.FromResult<IReadOnlyList<BestPractice>>(results);
    }

    public Task<IReadOnlyList<AccessibilityRule>> GetAccessibilityRulesAsync(CancellationToken ct)
    {
        EnsureLoaded();
        return Task.FromResult<IReadOnlyList<AccessibilityRule>>(_accessibilityRules.ToList());
    }

    public Task<IReadOnlyList<ReferenceUiPattern>> GetReferenceUiPatternsAsync(CancellationToken ct)
    {
        EnsureLoaded();
        return Task.FromResult<IReadOnlyList<ReferenceUiPattern>>(_referenceUiPatterns.ToList());
    }

    // ── Loading ──────────────────────────────────────────────────────────

    private void EnsureLoaded()
    {
        if (_loaded)
            return;

        lock (_loadLock)
        {
            if (_loaded)
                return;

            LoadAll();
            _loaded = true;
        }
    }

    private void LoadAll()
    {
        if (!Directory.Exists(_knowledgePath))
            throw new DirectoryNotFoundException($"Knowledge directory not found: {_knowledgePath}");

        var manifestPath = Path.Combine(_knowledgePath, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("Knowledge manifest not found.", manifestPath);

        var manifest = JObject.Parse(File.ReadAllText(manifestPath));

        _components = LoadList<ComponentDetail>(manifest["components"], "components");
        _tokens = LoadSingle<DesignTokenSet>(manifest["tokens"], "tokens");
        _layouts = LoadList<LayoutPattern>(manifest["layouts"], "layouts");
        _referenceUiPatterns = LoadList<ReferenceUiPattern>(manifest["referenceUiPatterns"], "referenceUiPatterns");
        _icons = LoadIconSet(manifest["icons"]);
        _bestPractices = LoadBestPractices(manifest["bestPractices"]);
        _accessibilityRules = LoadAccessibilityRules(manifest["accessibilityRules"]);
    }

    private List<T> LoadList<T>(JToken? pathsToken, string folder)
    {
        var result = new List<T>();
        if (pathsToken is not JArray paths)
            return result;

        foreach (var pathToken in paths)
        {
            var relativePath = pathToken.ToString();
            var fullPath = Path.Combine(_knowledgePath, relativePath);

            // Manifest entries can reference optional/static knowledge assets
            // (e.g. layouts, referenceUiPatterns) that a per-project trainer
            // run (KnowledgeTrainerService.UpdateManifest) may not have
            // generated yet in a freshly-trained consumer project's Knowledge
            // Base. Skip rather than fail the whole load so one missing
            // optional file doesn't break every tool call — consistent with
            // the already-lenient icons/best-practices/accessibility loaders
            // below.
            if (!File.Exists(fullPath))
                continue;

            var item = JsonConvert.DeserializeObject<T>(File.ReadAllText(fullPath));
            if (item is not null)
                result.Add(item);
        }

        return result;
    }

    private T LoadSingle<T>(JToken? pathToken, string label)
    {
        if (pathToken is null)
            return Activator.CreateInstance<T>();

        var fullPath = Path.Combine(_knowledgePath, pathToken.ToString());
        if (!File.Exists(fullPath))
            return Activator.CreateInstance<T>();

        return JsonConvert.DeserializeObject<T>(File.ReadAllText(fullPath))
            ?? Activator.CreateInstance<T>();
    }

    private List<Icon> LoadIconSet(JToken? pathToken)
    {
        if (pathToken is null)
            return new List<Icon>();

        var fullPath = Path.Combine(_knowledgePath, pathToken.ToString());
        if (!File.Exists(fullPath))
            return new List<Icon>();

        var json = JObject.Parse(File.ReadAllText(fullPath));
        return json["icons"]?
            .Select(i => i.ToObject<Icon>())
            .Where(i => i is not null)
            .Cast<Icon>()
            .ToList() ?? new List<Icon>();
    }

    private List<AccessibilityRule> LoadAccessibilityRules(JToken? pathToken)
    {
        if (pathToken is null)
            return new List<AccessibilityRule>();

        var fullPath = Path.Combine(_knowledgePath, pathToken.ToString());
        if (!File.Exists(fullPath))
            return new List<AccessibilityRule>();

        var json = JObject.Parse(File.ReadAllText(fullPath));
        return json["rules"]?
            .Select(r => r.ToObject<AccessibilityRule>())
            .Where(r => r is not null)
            .Cast<AccessibilityRule>()
            .ToList() ?? new List<AccessibilityRule>();
    }

    private List<BestPractice> LoadBestPractices(JToken? pathsToken)
    {
        var result = new List<BestPractice>();
        if (pathsToken is not JArray paths)
            return result;

        foreach (var pathToken in paths)
        {
            var relativePath = pathToken.ToString();
            var fullPath = Path.Combine(_knowledgePath, relativePath);
            if (!File.Exists(fullPath))
                continue;

            var (frontmatter, body) = ParseMarkdownWithFrontmatter(File.ReadAllText(fullPath));
            result.Add(new BestPractice
            {
                Id = frontmatter.GetValueOrDefault("id") ?? Path.GetFileNameWithoutExtension(fullPath),
                Title = frontmatter.GetValueOrDefault("title") ?? "Untitled",
                Tags = (frontmatter.GetValueOrDefault("tags") ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(t => t.Trim('[', ']', '"', '\''))
                    .ToList(),
                Content = body.Trim()
            });
        }

        return result;
    }

    private static (Dictionary<string, string> frontmatter, string body) ParseMarkdownWithFrontmatter(string content)
    {
        var frontmatter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!content.StartsWith("---", StringComparison.Ordinal))
            return (frontmatter, content);

        var endMarker = content.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (endMarker < 0)
            return (frontmatter, content);

        var yamlBlock = content.Substring(3, endMarker - 3).Trim();
        var body = content.Substring(endMarker + 4).TrimStart();

        foreach (var line in yamlBlock.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex > 0)
            {
                var key = line.Substring(0, colonIndex).Trim();
                var value = line.Substring(colonIndex + 1).Trim();
                frontmatter[key] = value;
            }
        }

        return (frontmatter, body);
    }
}
