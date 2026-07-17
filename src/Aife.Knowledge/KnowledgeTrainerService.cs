using System.Text.RegularExpressions;
using Aife.Application.Knowledge;
using Newtonsoft.Json;

namespace Aife.Knowledge;

/// <summary>
/// Scans a source code repository for design tokens (SCSS variables) and
/// component definitions (CustomUI .tsx/.jsx files). Writes the discovered
/// knowledge into the knowledge/ directory structure.
/// </summary>
public sealed class KnowledgeTrainerService : IKnowledgeTrainer
{
    private static readonly Regex ScssVariableRegex = new(
        @"\$([\w-]+)\s*:\s*([^;]+);",
        RegexOptions.Compiled);

    private static readonly Regex CssCustomPropertyRegex = new(
        @"--([\w-]+)\s*:\s*([^;]+);",
        RegexOptions.Compiled);

    private static readonly Regex TokenValueRegex = new(
        @"^(#[0-9a-fA-F]{3,8}|[0-9]+px|[0-9]+%|rgba?\([^)]+\)|[\d.]+(?:rem|em|vh|vw)|[a-z-]+\([^)]*\)).*$",
        RegexOptions.Compiled);

    private static readonly Regex TsxPropsRegex = new(
        @"interface\s+I(\w+)Props\b.*?\{([^}]+)\}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex PropLineRegex = new(
        @"(\w+)\??\s*:\s*(\w+(?:<[^>]+>)?)(?:;|,)",
        RegexOptions.Compiled);

    // Matches `X.propTypes = { ... };` blocks used by plain JS/JSX components
    // (BUS core repo convention: PropTypes instead of TS interfaces).
    private static readonly Regex PropTypesBlockRegex = new(
        @"(\w+)\.propTypes\s*=\s*\{([\s\S]*?)\}\s*;",
        RegexOptions.Compiled);

    // Matches a single top-level `propName: PropTypes.xxx` declaration.
    private static readonly Regex PropTypesNameRegex = new(
        @"^(\w+)\s*:\s*PropTypes\.(\w+)",
        RegexOptions.Compiled);

    // Group 1 = "default " or empty; Group 2 = component name.
    // Covers `export const X = forwardRef(...)`, `export function X(...)`,
    // `export default function X(...)`, and `export default class X`.
    private static readonly Regex ComponentExportRegex = new(
        @"export\s+(default\s+)?(?:const|function|class)\s+(\w+)",
        RegexOptions.Compiled);

    // Covers `export default X;` (component declared earlier, exported by name at the bottom).
    private static readonly Regex DefaultExportNameRegex = new(
        @"export\s+default\s+(\w+)\s*;",
        RegexOptions.Compiled);

    private static readonly Dictionary<string, (string componentId, string name, string category)> TokenCategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["color"] = ("color", "Color", "color"),
        ["spacing"] = ("spacing", "Spacing", "spacing"),
        ["radius"] = ("radius", "Border Radius", "radius"),
        ["shadow"] = ("shadow", "Box Shadow", "shadow"),
        ["font-size"] = ("typography", "Font Size", "typography"),
        ["font-weight"] = ("typography", "Font Weight", "typography"),
        ["font-family"] = ("typography", "Font Family", "typography"),
        ["breakpoint"] = ("breakpoint", "Breakpoint", "layout"),
        ["z-index"] = ("z-index", "Z-Index", "layout"),
        ["transition"] = ("animation", "Transition", "animation"),
        ["border"] = ("border", "Border", "border"),
    };

    private readonly string _knowledgePath;

    public KnowledgeTrainerService(string knowledgePath)
    {
        _knowledgePath = knowledgePath;
    }

    public Task<TrainResult> TrainFromFolderAsync(string folderPath, TrainMode mode, CancellationToken ct)
    {
        var result = new TrainResult { Success = true };
        var warnings = new List<string>();
        var errors = new List<string>();

        try
        {
            ct.ThrowIfCancellationRequested();

            // 1. Find Variables.scss (design tokens)
            var variablesPath = FindVariablesFile(folderPath);
            if (variablesPath is not null)
            {
                var tokens = ExtractTokens(variablesPath);
                WriteTokensFile(tokens);
                result = result with { TokensExtracted = tokens.Count };
            }
            else
            {
                warnings.Add("No Variables.scss found. Looking for *.scss with variable definitions...");
                var altVars = FindAllScssFiles(folderPath).Where(HasVariables).ToList();
                if (altVars.Count > 0)
                {
                    var allTokens = new List<DesignToken>();
                    foreach (var f in altVars)
                        allTokens.AddRange(ExtractTokens(f));
                    WriteTokensFile(allTokens);
                    result = result with { TokensExtracted = allTokens.Count };
                }
                else
                {
                    warnings.Add("No SCSS variable files found. Tokens not updated.");
                }
            }

            // 2. Find component definitions (CustomUI folder)
            if (mode == TrainMode.Replace)
            {
                // Clean ALL existing component files for a fresh rebuild
                var compDir = Path.Combine(_knowledgePath, "components");
                if (Directory.Exists(compDir))
                {
                    foreach (var file in Directory.GetFiles(compDir, "*.json"))
                    {
                        try { File.Delete(file); } catch { /* skip locked */ }
                    }
                }
            }

            var componentFiles = FindComponentFiles(folderPath);
            var extracted = new List<ComponentDetail>();
            foreach (var componentFile in componentFiles)
            {
                var file = componentFile.Path;
                try
                {
                    var component = ExtractComponent(file, componentFile.IsNested);
                    if (component is not null)
                        extracted.Add(component);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Could not extract component from {Path.GetFileName(file)}: {ex.Message}");
                }
            }

            var components = DemoteInternalSubComponents(extracted);
            foreach (var component in components)
                WriteComponentFile(component);

            result = result with { ComponentsExtracted = components.Count };

            // 3. Update manifest
            //    Replace mode: overwrite manifest with only discovered components
            //    Update mode: merge with existing, keep existing entries
            UpdateManifest(components, mode);

            result = result with
            {
                Warnings = warnings,
                Errors = errors,
                KnowledgeBasePath = _knowledgePath
            };
        }
        catch (OperationCanceledException)
        {
            result = result with { Success = false };
            errors.Add("Training was cancelled.");
            result = result with { Errors = errors };
        }
        catch (Exception ex)
        {
            result = result with { Success = false };
            errors.Add(ex.Message);
            result = result with { Errors = errors };
        }

        return Task.FromResult(result);
    }

    public async Task<TrainResult> TrainFromGitAsync(string gitUrl, string? branch, TrainMode mode, CancellationToken ct)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"aife-kb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var branchArg = !string.IsNullOrWhiteSpace(branch) ? $"-b {branch}" : "";
            var cloneArgs = $"clone --depth 1 {branchArg} {gitUrl} \"{tempDir}\"";

            var psi = new System.Diagnostics.ProcessStartInfo("git", cloneArgs)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
                return new TrainResult { Success = false, Errors = new List<string> { "Failed to start git process." } };

            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                var err = await process.StandardError.ReadToEndAsync(ct);
                return new TrainResult { Success = false, Errors = new List<string> { $"Git clone failed: {err}" } };
            }

            return await TrainFromFolderAsync(tempDir, mode, ct);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* cleanup best-effort */ }
        }
    }

    // ── Private helpers ──

    private static string? FindVariablesFile(string folderPath)
    {
        // Look for Variables.scss in Styles/Basic/
        var patterns = new[]
        {
            Path.Combine(folderPath, "src", "Styles", "Basic", "Variables.scss"),
            Path.Combine(folderPath, "src", "styles", "basic", "Variables.scss"),
            Path.Combine(folderPath, "src", "Styles", "Variables.scss"),
        };

        foreach (var p in patterns)
        {
            if (File.Exists(p))
                return p;
        }

        // Fallback: deep search
        return Directory.EnumerateFiles(folderPath, "Variables.scss", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private static IEnumerable<string> FindAllScssFiles(string folderPath)
    {
        try
        {
            return Directory.EnumerateFiles(folderPath, "*.scss", SearchOption.AllDirectories);
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    private static bool HasVariables(string filePath)
    {
        try
        {
            var content = File.ReadAllText(filePath);
            return ScssVariableRegex.IsMatch(content) || CssCustomPropertyRegex.IsMatch(content);
        }
        catch
        {
            return false;
        }
    }

    private static List<DesignToken> ExtractTokens(string variablesPath)
    {
        var tokens = new List<DesignToken>();
        var content = File.ReadAllText(variablesPath);

        foreach (Match match in ScssVariableRegex.Matches(content))
        {
            var name = match.Groups[1].Value.Trim();
            var value = match.Groups[2].Value.Trim();

            // Skip non-token variables (like @imports or complex expressions)
            if (!TokenValueRegex.IsMatch(value))
                continue;

            var category = ClassifyToken(name);
            tokens.Add(new DesignToken
            {
                Name = name,
                Value = value,
                Category = category,
                Description = $"Extracted from {Path.GetFileName(variablesPath)}"
            });
        }

        // Some projects declare tokens as CSS custom properties (--name: value;)
        // inside :root {} instead of, or alongside, SCSS $variables.
        foreach (Match match in CssCustomPropertyRegex.Matches(content))
        {
            var name = match.Groups[1].Value.Trim();
            var value = match.Groups[2].Value.Trim();

            if (!TokenValueRegex.IsMatch(value))
                continue;

            var category = ClassifyToken(name);
            tokens.Add(new DesignToken
            {
                Name = name,
                Value = value,
                Category = category,
                Description = $"Extracted from {Path.GetFileName(variablesPath)}"
            });
        }

        return tokens.GroupBy(t => t.Name).Select(g => g.First()).ToList();
    }

    private static string ClassifyToken(string name)
    {
        var lower = name.ToLowerInvariant();

        if (lower.StartsWith("color") || lower.Contains("color") || lower.Contains("colour"))
            return "color";
        if (lower.StartsWith("sp") || lower.Contains("spacing") || lower.Contains("padding") || lower.Contains("margin"))
            return "spacing";
        if (lower.Contains("radius") || lower.Contains("rounded"))
            return "radius";
        if (lower.Contains("shadow"))
            return "shadow";
        if (lower.Contains("font-size") || lower.StartsWith("font-h") || lower.StartsWith("font-title") || lower.StartsWith("font-p"))
            return "typography";
        if (lower.Contains("font-weight"))
            return "typography";
        if (lower.Contains("font-family") || lower.Contains("font-stack"))
            return "typography";
        if (lower.Contains("border"))
            return "border";
        // The design-token-set schema only allows: color, spacing, radius,
        // typography, border, shadow. Sizing-ish concepts without a better
        // home (layout widths/breakpoints, z-index, transitions/animation)
        // are bucketed under "spacing" rather than an invalid category.
        if (lower.Contains("width") || lower.Contains("breakpoint")
            || lower.Contains("z-index") || lower.Contains("transition") || lower.Contains("animation"))
            return "spacing";

        return "spacing";
    }

    /// <summary>
    /// Demotes components whose id is a PascalCase extension of another
    /// extracted component's id (e.g. BUSGridTable, BUSGridColumnMenu relative
    /// to BUSGrid) to a neutral category with no HTML mapping. Folder depth
    /// alone can't reliably identify internal sub-pieces (some real public
    /// components, like bus-grids/bus-grid/bus-grid.jsx, are themselves one
    /// level deep), so this instead looks at naming: if the id equals a
    /// shorter sibling id plus additional capitalized word(s), it's very
    /// likely an internal implementation detail of that sibling and shouldn't
    /// compete with it for HTML-element matching (e.g. "table" -> BUSGrid).
    /// </summary>
    private static List<ComponentDetail> DemoteInternalSubComponents(List<ComponentDetail> components)
    {
        var ids = components.Select(c => c.ComponentId).ToList();

        return components
            .Select(c =>
            {
                var isSubPieceOfAnother = ids.Any(otherId =>
                    !otherId.Equals(c.ComponentId, StringComparison.Ordinal)
                    && otherId.Length < c.ComponentId.Length
                    && c.ComponentId.StartsWith(otherId, StringComparison.Ordinal)
                    // Guard against unrelated ids that happen to share a
                    // prefix (e.g. BUSButton vs BUSButtonGroup would still be
                    // considered related here, which is the desired
                    // behavior); require the next character to start a new
                    // PascalCase word so "BUSGrid" doesn't also swallow an
                    // unrelated "BUSGridley".
                    && char.IsUpper(c.ComponentId[otherId.Length]));

                return isSubPieceOfAnother
                    ? c with { Category = "other", MapsFromHtml = new List<string>() }
                    : c;
            })
            .ToList();
    }

    private static List<(string Path, bool IsNested)> FindComponentFiles(string folderPath)
    {
        var results = new List<(string Path, bool IsNested)>();

        // Prefer the canonical CustomUIs convention. Only fall back to a plain
        // Components/ scan when CustomUIs doesn't exist — scanning both would
        // double-count every file under CustomUIs (it's nested inside
        // Components). Case-insensitive file systems (Windows) also mean
        // "Components" and "components" resolve to the same physical folder,
        // so only the first existing candidate in each group is scanned.
        //
        // `folderPath` itself is accepted as either a repo root (containing
        // src/Components/CustomUIs) OR a path that already points directly at
        // the Components folder — the convention used by
        // --components-source/AIFE_COMPONENTS_SOURCE in .mcp.json configs
        // (e.g. "...\Repo\src\Components"). Without the direct-folder
        // candidates below, that convention silently double-nests the path
        // (".../src/Components/src/Components/CustomUIs") and finds nothing.
        var customUisPath = new[]
        {
            Path.Combine(folderPath, "src", "Components", "CustomUIs"),
            Path.Combine(folderPath, "src", "components", "CustomUIs"),
            Path.Combine(folderPath, "CustomUIs"),
        }.FirstOrDefault(Directory.Exists);

        if (customUisPath is not null)
        {
            ScanComponentFolder(customUisPath, results);
        }
        else
        {
            var componentsPath = new[]
            {
                Path.Combine(folderPath, "src", "Components"),
                Path.Combine(folderPath, "src", "components"),
                folderPath,
            }.FirstOrDefault(Directory.Exists);

            if (componentsPath is not null)
                ScanComponentFolder(componentsPath, results);
        }

        // Defense in depth against case-sensitive file systems or any
        // remaining path overlap: de-dupe by physical path before filtering.
        return results
            .GroupBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Where(r => HasComponentExport(r.Path))
            .ToList();
    }

    /// <summary>
    /// Scans direct child folders of <paramref name="rootPath"/> (each = one DS
    /// component group) for .tsx/.jsx/.js files, recursing exactly one level
    /// deeper. BUS core repos mix both PascalCase (BUSButtons/BUSButton.js) and
    /// lowercase-hyphenated (bus-grids/bus-grid/bus-grid.jsx) folder
    /// conventions for the actual public component, so nested folders are not
    /// filtered by name — internal sub-pieces just become extra (harmless)
    /// entries tagged <c>IsNested = true</c> so callers can avoid classifying
    /// them as if they were the primary, HTML-mappable component.
    /// </summary>
    private static void ScanComponentFolder(string rootPath, List<(string Path, bool IsNested)> results)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(rootPath))
            {
                var topFiles = Directory.GetFiles(dir, "*.tsx")
                    .Concat(Directory.GetFiles(dir, "*.jsx"))
                    .Concat(Directory.GetFiles(dir, "*.js"));
                results.AddRange(topFiles.Select(f => (f, false)));

                foreach (var subDir in Directory.GetDirectories(dir))
                {
                    var subFiles = Directory.GetFiles(subDir, "*.tsx")
                        .Concat(Directory.GetFiles(subDir, "*.jsx"))
                        .Concat(Directory.GetFiles(subDir, "*.js"));
                    results.AddRange(subFiles.Select(f => (f, true)));
                }
            }

            // Also include loose files at the root level
            results.AddRange(Directory.GetFiles(rootPath, "*.tsx").Select(f => (f, false)));
            results.AddRange(Directory.GetFiles(rootPath, "*.jsx").Select(f => (f, false)));
            results.AddRange(Directory.GetFiles(rootPath, "*.js").Select(f => (f, false)));
        }
        catch { /* skip inaccessible directories */ }
    }

    private static bool HasComponentExport(string filePath)
    {
        try
        {
            var content = File.ReadAllText(filePath);
            return ComponentExportRegex.IsMatch(content)
                || DefaultExportNameRegex.IsMatch(content)
                || content.Contains("export default");
        }
        catch
        {
            return false;
        }
    }

    private static ComponentDetail? ExtractComponent(string filePath, bool isNested = false)
    {
        var content = File.ReadAllText(filePath);
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var folderName = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? fileName;

        // Resolve the exported component name and whether it's a default export.
        var (componentName, exportName, isDefaultExport) = ResolveExportInfo(content, fileName);

        // Generate a stable componentId
        var componentId = componentName.StartsWith("BUS") ? componentName : $"BUS{componentName}";

        // Note: folder depth alone ("isNested") doesn't reliably distinguish
        // internal sub-pieces from the real public component — some BUS
        // components (e.g. bus-grids/bus-grid/bus-grid.jsx) legitimately live
        // one level deep too. Internal-piece demotion instead happens as a
        // post-processing pass (see DemoteInternalSubComponents) based on
        // componentId prefix relationships within the whole extracted batch.
        var category = ClassifyComponent(componentId, folderName, content);
        var mapsFromHtml = InferMapsFromHtml(componentId, content);

        // Extract props: prefer a TypeScript interface (I*Props); fall back to
        // PropTypes.* (the BUS core repo's plain JS/JSX convention).
        var props = new List<ComponentProp>();
        var interfaceMatch = TsxPropsRegex.Match(content);
        if (interfaceMatch.Success)
        {
            var propsBlock = interfaceMatch.Groups[2].Value;
            foreach (Match pm in PropLineRegex.Matches(propsBlock))
            {
                var propName = pm.Groups[1].Value.Trim();
                var propType = pm.Groups[2].Value.Trim();
                props.Add(new ComponentProp
                {
                    Name = propName,
                    Type = MapType(propType),
                    Required = !propName.EndsWith("?")
                });
            }
        }
        else
        {
            props = ExtractPropTypesProps(content);
        }

        // Infer tokens consumed from the component code
        var tokensConsumed = InferTokensConsumed(content, componentId);

        var importPath = ComputeImportPath(filePath);

        return new ComponentDetail
        {
            ComponentId = componentId,
            Name = ToHumanName(componentName),
            Category = category,
            Status = "approved",
            Description = $"Extracted from {Path.GetRelativePath(Path.GetDirectoryName(filePath)!, filePath)}",
            Props = props,
            TokensConsumed = tokensConsumed,
            MapsFromHtml = mapsFromHtml,
            ImportPath = importPath,
            ExportName = exportName,
            IsDefaultExport = isDefaultExport,
            SourceFile = "src/" + importPath + Path.GetExtension(filePath)
        };
    }

    /// <summary>
    /// Resolves the exported component name, the recommended import name, and
    /// whether it's exported as default. Handles both TS/named-export style
    /// (<c>export const X = forwardRef(...)</c>, <c>export function X(...)</c>)
    /// and default-export styles (<c>export default function X(...)</c>,
    /// <c>export default X;</c>, or an anonymous default export).
    /// </summary>
    private static (string componentName, string exportName, bool isDefaultExport) ResolveExportInfo(string content, string fileName)
    {
        var namedMatch = ComponentExportRegex.Match(content);
        if (namedMatch.Success)
        {
            var isDefault = namedMatch.Groups[1].Success && namedMatch.Groups[1].Value.Trim().Length > 0;
            var name = namedMatch.Groups[2].Value;
            return (name, name, isDefault);
        }

        var defaultNameMatch = DefaultExportNameRegex.Match(content);
        if (defaultNameMatch.Success)
        {
            var name = defaultNameMatch.Groups[1].Value;
            return (name, name, true);
        }

        // Anonymous default export or no recognizable export pattern — fall
        // back to the file name (the common convention for the component name).
        return (fileName, fileName, true);
    }

    /// <summary>
    /// Extracts props from a <c>Component.propTypes = { ... };</c> block —
    /// the convention used by plain JS/JSX components (no TS interfaces).
    /// Splits the block on top-level (bracket-depth-aware) commas so nested
    /// shapes like <c>PropTypes.arrayOf(PropTypes.shape({...}))</c> don't
    /// break the split.
    /// </summary>
    private static List<ComponentProp> ExtractPropTypesProps(string content)
    {
        var props = new List<ComponentProp>();
        var blockMatch = PropTypesBlockRegex.Match(content);
        if (!blockMatch.Success)
            return props;

        var body = blockMatch.Groups[2].Value;
        foreach (var statement in SplitTopLevel(body, ','))
        {
            var trimmed = statement.Trim();
            if (trimmed.Length == 0)
                continue;

            var m = PropTypesNameRegex.Match(trimmed);
            if (!m.Success)
                continue;

            var propName = m.Groups[1].Value;
            var propType = m.Groups[2].Value;
            var required = trimmed.Contains(".isRequired");

            props.Add(new ComponentProp
            {
                Name = propName,
                Type = MapPropTypesType(propType),
                Required = required
            });
        }

        return props;
    }

    /// <summary>
    /// Splits <paramref name="content"/> on <paramref name="separator"/> only at
    /// bracket depth 0, so nested parens/braces/brackets (e.g. PropTypes.shape({...}))
    /// aren't split apart.
    /// </summary>
    private static List<string> SplitTopLevel(string content, char separator)
    {
        var results = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (c == separator && depth == 0)
            {
                results.Add(content[start..i]);
                start = i + 1;
            }
        }

        if (start < content.Length)
            results.Add(content[start..]);

        return results;
    }

    private static string MapPropTypesType(string propType) => propType.ToLowerInvariant() switch
    {
        "string" => "string",
        "number" => "number",
        "bool" => "boolean",
        "func" => "function",
        "node" => "ReactNode",
        "element" => "ReactNode",
        "array" => "array",
        "arrayof" => "array",
        "object" => "object",
        "shape" => "object",
        "exact" => "object",
        "oneoftype" => "any",
        "oneof" => "string",
        "instanceof" => "any",
        _ => "any"
    };

    /// <summary>
    /// Computes an import path relative to the project's <c>src/</c> folder
    /// (e.g. <c>Components/CustomUIs/BUSButtons/BUSButton</c>, no extension),
    /// so the caller can write a real <c>import ... from '...'</c> statement.
    /// </summary>
    private static string ComputeImportPath(string filePath)
    {
        var normalized = filePath.Replace('\\', '/');
        var srcIndex = normalized.LastIndexOf("/src/", StringComparison.OrdinalIgnoreCase);
        var relative = srcIndex >= 0 ? normalized[(srcIndex + 5)..] : Path.GetFileName(normalized);
        return Path.ChangeExtension(relative, null) ?? relative;
    }

    private static string ClassifyComponent(string componentId, string folderName, string content)
    {
        var lower = componentId.ToLowerInvariant();
        var folder = folderName.ToLowerInvariant();
        var contentLower = content.ToLowerInvariant();

        if ((lower.Contains("button") && !lower.Contains("radio")) || folder.Contains("button")) return "button";
        if (lower.Contains("grid") || lower.Contains("table") || folder.Contains("grid")) return "table";
        if (lower.Contains("tab") || folder.Contains("tab") || lower.Contains("nav")) return "navigation";
        if (lower.Contains("input") || lower.Contains("search") || lower.Contains("textbox")
            || folder.Contains("input")) return "input";
        if (lower.Contains("form") || lower.Contains("field") || folder.Contains("form")) return "form";
        if (lower.Contains("dialog") || lower.Contains("modal") || folder.Contains("dialog")) return "dialog";
        if (lower.Contains("checkbox") || lower.Contains("check")) return "form";
        if (lower.Contains("switch") || lower.Contains("toggle")) return "form";
        if (lower.Contains("label") || lower.Contains("badge") || lower.Contains("chip")) return "display";
        if (lower.Contains("layout") || lower.Contains("shell")) return "layout";
        if (lower.Contains("dropdown") || lower.Contains("select") || lower.Contains("multi")) return "input";

        return "other";
    }

    private static string MapType(string tsType) => tsType.ToLowerInvariant() switch
    {
        "string" => "string",
        "number" => "number",
        "boolean" => "boolean",
        "reactnode" or "react.reactnode" => "ReactNode",
        "function" => "function",
        "void" => "function",
        _ => "any"
    };

    private static List<string> InferTokensConsumed(string content, string componentId)
    {
        var tokens = new List<string>();

        // Scan for var(--color-...) CSS variable references
        var cssVarRegex = new Regex(@"var\(--([\w-]+)\)");
        foreach (Match m in cssVarRegex.Matches(content))
        {
            tokens.Add("color." + m.Groups[1].Value.Replace("color-", ""));
        }

        // Add standard tokens based on component type
        if (componentId.Contains("Button"))
        {
            tokens.Add("color.primary");
            tokens.Add("color.primary.hover");
            tokens.Add("color.text.white");
            tokens.Add("spacing.xs");
            tokens.Add("radius.md");
        }
        if (componentId.Contains("Grid") || componentId.Contains("DataGrid"))
        {
            tokens.Add("color.table.header.bg");
            tokens.Add("color.text.primary");
            tokens.Add("color.table.row.border");
        }

        return tokens.Distinct().ToList();
    }

    private static List<string> InferMapsFromHtml(string componentId, string content)
    {
        var maps = new List<string>();
        var lower = componentId.ToLowerInvariant();

        if (lower.Contains("button") && !lower.Contains("radio")) maps.AddRange(new[] { "button" });
        if (lower.Contains("grid") || lower.Contains("table")) maps.AddRange(new[] { "table" });
        if (lower.Contains("tab")) maps.AddRange(new[] { "tabs", "tab" });
        if (lower.Contains("input") || lower.Contains("search")) maps.AddRange(new[] { "input", "search" });
        if (lower.Contains("checkbox")) maps.AddRange(new[] { "checkbox" });
        if (lower.Contains("switch") || lower.Contains("toggle")) maps.AddRange(new[] { "switch" });
        if (lower.Contains("form") || lower.Contains("field")) maps.AddRange(new[] { "form", "formfield" });

        return maps;
    }

    private static string ToHumanName(string componentName)
    {
        // BUSButton → BUS Button, DataGrid → Data Grid
        return Regex.Replace(componentName, "([a-z])([A-Z])", "$1 $2");
    }

    private void WriteTokensFile(List<DesignToken> tokens)
    {
        var filePath = Path.Combine(_knowledgePath, "tokens", "tokens.json");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        var json = JsonConvert.SerializeObject(new { tokens }, new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
        });
        File.WriteAllText(filePath, json);
    }

    private void WriteComponentFile(ComponentDetail component)
    {
        var dir = Path.Combine(_knowledgePath, "components");
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, $"{component.ComponentId}.json");

        // Omit null variants/examples/accessibility rather than serializing
        // explicit `null` — the component catalog schema types these fields
        // as array/object (not nullable), so a present-but-null value fails
        // validation while an absent property is fine (none are required).
        var json = JsonConvert.SerializeObject(component, new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        });
        File.WriteAllText(filePath, json);
    }

    private void UpdateManifest(List<ComponentDetail> components, TrainMode mode)
    {
        var unique = components
            .GroupBy(c => c.ComponentId)
            .Select(g => g.First())
            .OrderBy(c => c.ComponentId)
            .ToList();

        var newEntries = unique.Select(c => $"components/{c.ComponentId}.json").ToList();

        List<string> merged;

        if (mode == TrainMode.Replace)
        {
            // Replace: only discovered components
            merged = newEntries;
        }
        else
        {
            // Update: merge with existing — keep old components not found in this scan
            var existingEntries = LoadExistingManifestComponents();
            merged = existingEntries
                .Concat(newEntries)
                .Distinct()
                .OrderBy(x => x)
                .ToList();
        }

        var filePath = Path.Combine(_knowledgePath, "manifest.json");

        // The trainer only ever writes tokens/*.json and components/*.json
        // into _knowledgePath (see TrainFromFolderAsync above) — it never
        // generates layouts/, referenceUiPatterns/, icons/, best-practices/
        // or accessibility/ files for a consumer project. Pointing the
        // manifest at those static, dev-repo-only asset paths regardless
        // makes JsonKnowledgeProvider fail to load the Knowledge Base the
        // moment a project is trained without them (e.g. a fresh
        // `.aife/knowledge` for a consumer repo). Only reference an asset
        // path if the file actually exists under _knowledgePath already
        // (pre-seeded or from a prior train); otherwise omit it so the
        // provider treats it as simply not configured.
        var manifest = new
        {
            version = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"),
            components = merged,
            tokens = ExistingKnowledgeFileOrDefault("tokens/tokens.json"),
            layouts = ExistingKnowledgeFilesOrEmpty("layouts/AppLayout.json"),
            referenceUiPatterns = ExistingKnowledgeFilesOrEmpty("referenceUiPatterns/ActiveInspectionsPage.json"),
            icons = ExistingKnowledgeFileOrDefault("icons/icons.json"),
            bestPractices = ExistingKnowledgeFilesOrEmpty("best-practices/naming.md"),
            accessibilityRules = ExistingKnowledgeFileOrDefault("accessibility/rules.json")
        };

        var json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Returns the relative path if the file exists under _knowledgePath,
    /// otherwise null (manifest key is omitted / provider falls back to a
    /// default empty value instead of a broken reference).
    /// </summary>
    private string? ExistingKnowledgeFileOrDefault(string relativePath) =>
        File.Exists(Path.Combine(_knowledgePath, relativePath)) ? relativePath : null;

    /// <summary>
    /// Same as <see cref="ExistingKnowledgeFileOrDefault"/> but for manifest
    /// keys that hold an array of paths.
    /// </summary>
    private string[] ExistingKnowledgeFilesOrEmpty(string relativePath) =>
        File.Exists(Path.Combine(_knowledgePath, relativePath)) ? new[] { relativePath } : Array.Empty<string>();

    private List<string> LoadExistingManifestComponents()
    {
        var filePath = Path.Combine(_knowledgePath, "manifest.json");
        if (!File.Exists(filePath))
            return new List<string>();

        try
        {
            var existing = JsonConvert.DeserializeAnonymousType(
                File.ReadAllText(filePath),
                new { components = new List<string>() });
            return existing?.components ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}
