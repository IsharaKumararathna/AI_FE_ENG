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

    private static readonly Regex TokenValueRegex = new(
        @"^(#[0-9a-fA-F]{3,8}|[0-9]+px|[0-9]+%|rgba?\([^)]+\)|[\d.]+(?:rem|em|vh|vw)|[a-z-]+\([^)]*\)).*$",
        RegexOptions.Compiled);

    private static readonly Regex TsxPropsRegex = new(
        @"interface\s+I(\w+)Props\b.*?\{([^}]+)\}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex PropLineRegex = new(
        @"(\w+)\??\s*:\s*(\w+(?:<[^>]+>)?)(?:;|,)",
        RegexOptions.Compiled);

    private static readonly Regex ComponentExportRegex = new(
        @"export\s+(?:const|function|class)\s+(\w+)",
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
            var components = new List<ComponentDetail>();
            foreach (var file in componentFiles)
            {
                try
                {
                    var component = ExtractComponent(file);
                    if (component is not null)
                    {
                        components.Add(component);
                        WriteComponentFile(component);
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Could not extract component from {Path.GetFileName(file)}: {ex.Message}");
                }
            }
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
            return ScssVariableRegex.IsMatch(content);
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

        return tokens;
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
        if (lower.Contains("width") || lower.Contains("breakpoint"))
            return "layout";
        if (lower.Contains("z-index"))
            return "layout";
        if (lower.Contains("transition") || lower.Contains("animation"))
            return "animation";
        if (lower.Contains("border"))
            return "border";

        return "other";
    }

    private static List<string> FindComponentFiles(string folderPath)
    {
        var results = new List<string>();
        var searchPaths = new[]
        {
            Path.Combine(folderPath, "src", "Components", "CustomUIs"),
            Path.Combine(folderPath, "src", "components", "CustomUIs"),
            Path.Combine(folderPath, "src", "Components"),
            Path.Combine(folderPath, "src", "components"),
        };

        foreach (var sp in searchPaths)
        {
            if (!Directory.Exists(sp))
                continue;

            try
            {
                // Scan direct child folders of CustomUIs (each = one DS component group).
                // For each child folder: pick .tsx/.jsx files at the root level,
                // but skip nested sub-folders (those are internal pieces, not DS components).
                foreach (var dir in Directory.GetDirectories(sp))
                {
                    // Collect files directly in this component folder (not in sub-folders)
                    var topFiles = Directory.GetFiles(dir, "*.tsx")
                        .Concat(Directory.GetFiles(dir, "*.jsx"))
                        .Concat(Directory.GetFiles(dir, "*.js"));
                    results.AddRange(topFiles);

                    // Only recurse into BUS sub-folders if this parent folder is NOT
                    // a BUS component folder itself (avoid double-counting).
                    var parentName = Path.GetFileName(dir);
                    foreach (var subDir in Directory.GetDirectories(dir))
                    {
                        var subName = Path.GetFileName(subDir);
                        // Skip internal sub-components like bus-grids/bus-grid/, BUSButtons/BUSDropdownButton/
                        if (subName.StartsWith("bus-", StringComparison.OrdinalIgnoreCase)
                            || (parentName.StartsWith("BUS", StringComparison.OrdinalIgnoreCase)
                                && subName.StartsWith("BUS", StringComparison.OrdinalIgnoreCase)))
                            continue;

                        var subFiles = Directory.GetFiles(subDir, "*.tsx")
                            .Concat(Directory.GetFiles(subDir, "*.jsx"))
                            .Concat(Directory.GetFiles(subDir, "*.js"));
                        results.AddRange(subFiles);
                    }
                }

                // Also include loose files at the CustomUIs root level
                results.AddRange(Directory.GetFiles(sp, "*.tsx"));
                results.AddRange(Directory.GetFiles(sp, "*.jsx"));
                results.AddRange(Directory.GetFiles(sp, "*.js"));
            }
            catch { /* skip inaccessible directories */ }
        }

        // Filter to only files that export a component
        return results.Where(HasComponentExport).ToList();
    }

    private static bool HasComponentExport(string filePath)
    {
        try
        {
            return ComponentExportRegex.IsMatch(File.ReadAllText(filePath));
        }
        catch
        {
            return false;
        }
    }

    private static ComponentDetail? ExtractComponent(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var folderName = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? fileName;

        // Extract component name
        var exportMatch = ComponentExportRegex.Match(content);
        var componentName = exportMatch.Success ? exportMatch.Groups[1].Value : fileName;

        // Generate a stable componentId
        var componentId = componentName.StartsWith("BUS") ? componentName : $"BUS{componentName}";

        // Determine category from folder/file name
        var category = ClassifyComponent(componentId, folderName, content);

        // Extract props from TypeScript interface
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

        // Infer tokens consumed from the component code
        var tokensConsumed = InferTokensConsumed(content, componentId);

        return new ComponentDetail
        {
            ComponentId = componentId,
            Name = ToHumanName(componentName),
            Category = category,
            Status = "approved",
            Description = $"Extracted from {Path.GetRelativePath(Path.GetDirectoryName(filePath)!, filePath)}",
            Props = props,
            TokensConsumed = tokensConsumed,
            MapsFromHtml = InferMapsFromHtml(componentId, content)
        };
    }

    private static string ClassifyComponent(string componentId, string folderName, string content)
    {
        var lower = componentId.ToLowerInvariant();
        var folder = folderName.ToLowerInvariant();
        var contentLower = content.ToLowerInvariant();

        if (lower.Contains("button") || folder.Contains("button")) return "button";
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

        if (lower.Contains("button")) maps.AddRange(new[] { "button" });
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

        var json = JsonConvert.SerializeObject(component, new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
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
        var manifest = new
        {
            version = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"),
            components = merged,
            tokens = "tokens/tokens.json",
            layouts = new[] { "layouts/AppLayout.json" },
            referenceUiPatterns = new[] { "referenceUiPatterns/ActiveInspectionsPage.json" },
            icons = "icons/icons.json",
            bestPractices = new[] { "best-practices/naming.md" },
            accessibilityRules = "accessibility/rules.json"
        };

        var json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
        File.WriteAllText(filePath, json);
    }

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
