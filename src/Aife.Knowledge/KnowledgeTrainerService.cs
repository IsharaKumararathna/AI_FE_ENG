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

    // ── Trainer v2: rich TS / DesignSystem extraction ──
    // `export type ButtonVariant = 'primary' | 'secondary' | ...;`
    private static readonly Regex TypeUnionRegex = new(
        @"export\s+type\s+(\w+)\s*=\s*([^;]+);",
        RegexOptions.Compiled);

    private static readonly Regex StringLiteralRegex = new(
        @"'([^']+)'",
        RegexOptions.Compiled);

    // SCSS sidecar token comments, e.g. `// token: colors.focus.ring`
    private static readonly Regex TokenCommentRegex = new(
        @"//\s*token:\s*([A-Za-z_][\w.\[\]]*)",
        RegexOptions.Compiled);

    // SCSS `$bus-ds-*` variable references.
    private static readonly Regex ScssVarRefRegex = new(
        @"\$(bus-ds-[\w-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // TS interface header with optional `extends` clause: captures name + extends.
    private static readonly Regex InterfaceHeaderRegex = new(
        @"(?:export\s+)?interface\s+(\w*Props)(?:\s+extends\s+([^{]+?))?\s*\{",
        RegexOptions.Compiled);

    // A single prop declaration line: `name?: Type` (optional `?` captured).
    private static readonly Regex PropDeclRegex = new(
        @"^(\w+)(\?)?\s*:\s*(.+)$",
        RegexOptions.Compiled);

    // Destructure defaults: `variant = 'primary'` / `loading = false` / `size = 2`.
    private static readonly Regex DestructureDefaultRegex = new(
        @"(\w+)\s*=\s*('([^']*)'|[\x22]([^\x22]*)[\x22]|true|false|-?\d+(?:\.\d+)?)",
        RegexOptions.Compiled);

    private static readonly Regex AriaAttrRegex = new(
        @"aria-([\w-]+)",
        RegexOptions.Compiled);

    private static readonly Regex RoleAttrRegex = new(
        @"role\s*=\s*[""']([\w-]+)[""']",
        RegexOptions.Compiled);

    private static readonly Regex FirstCodeLineRegex = new(
        @"^\s*(import|export|const|function|interface|type)\b",
        RegexOptions.Compiled | RegexOptions.Multiline);

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

            // 2b. Extract layout patterns (DesignSystem layout/ folder).
            //     Replace mode clears previously trained layouts first.
            if (mode == TrainMode.Replace)
            {
                var layoutDir = Path.Combine(_knowledgePath, "layouts");
                if (Directory.Exists(layoutDir))
                {
                    foreach (var f in Directory.GetFiles(layoutDir, "*.json"))
                        try { File.Delete(f); } catch { /* skip locked */ }
                }
            }
            var layouts = ExtractLayouts(folderPath);
            foreach (var layout in layouts)
                WriteLayoutFile(layout);

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
        // Known conventions in priority order — hit the most specific first.
        var patterns = new[]
        {
            // BUS legacy: repo-root -> src/Styles/Basic/Variables.scss
            Path.Combine(folderPath, "src", "Styles", "Basic", "Variables.scss"),
            Path.Combine(folderPath, "src", "styles", "basic", "Variables.scss"),
            Path.Combine(folderPath, "src", "Styles", "Variables.scss"),
            // DesignSystem (repo-root): src/DesignSystem/tokens/Variables.scss
            Path.Combine(folderPath, "src", "DesignSystem", "tokens", "Variables.scss"),
            Path.Combine(folderPath, "src", "DesignSystem", "Tokens", "Variables.scss"),
            Path.Combine(folderPath, "src", "DesignSystem", "styles", "Variables.scss"),
            Path.Combine(folderPath, "src", "DesignSystem", "Styles", "Variables.scss"),
            // DesignSystem (repo-root): src/DesignSystem/tokens/tokens.scss
            Path.Combine(folderPath, "src", "DesignSystem", "tokens", "tokens.scss"),
            Path.Combine(folderPath, "src", "DesignSystem", "Tokens", "tokens.scss"),
            // DesignSystem (Client-Core): src/DesignSystem/styles/_tokens.scss
            Path.Combine(folderPath, "src", "DesignSystem", "styles", "_tokens.scss"),
            Path.Combine(folderPath, "src", "DesignSystem", "Styles", "_tokens.scss"),
            Path.Combine(folderPath, "src", "DesignSystem", "tokens", "_tokens.scss"),
            // Direct DesignSystem path (--components-source points AT DesignSystem/)
            Path.Combine(folderPath, "tokens", "Variables.scss"),
            Path.Combine(folderPath, "Tokens", "Variables.scss"),
            Path.Combine(folderPath, "styles", "Variables.scss"),
            Path.Combine(folderPath, "Styles", "Variables.scss"),
            Path.Combine(folderPath, "tokens", "tokens.scss"),
            Path.Combine(folderPath, "Tokens", "tokens.scss"),
            // Direct DesignSystem path: Variables.scss at the root
            Path.Combine(folderPath, "Variables.scss"),
        };

        foreach (var p in patterns)
        {
            if (File.Exists(p))
                return p;
        }

        // Fallback: deep search (catches any naming convention, just slower)
        return Directory.EnumerateFiles(folderPath, "Variables.scss", SearchOption.AllDirectories)
            .FirstOrDefault()
            ?? Directory.EnumerateFiles(folderPath, "tokens.scss", SearchOption.AllDirectories)
               .FirstOrDefault(f => HasVariables(f))
            ?? Directory.EnumerateFiles(folderPath, "_tokens.scss", SearchOption.AllDirectories)
               .FirstOrDefault(f => HasVariables(f));
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

        // ── Canonical conventions, probed in priority order ──
        // The first existing folder wins. This prevents double-scanning
        // (CustomUIs lives inside Components, DesignSystem/components may
        // overlap with a Components folder, etc.).
        //
        // `folderPath` is accepted as either a repo root (containing
        // src/Components/CustomUIs or src/DesignSystem) OR a path that
        // already points directly at the components folder — the convention
        // used by --components-source/AIFE_COMPONENTS_SOURCE in .mcp.json
        // configs (e.g. "...\Repo\src\Components").
        var probePaths = new[]
        {
            // (1) BUS legacy: repo-root -> src/Components/CustomUIs/
            (ScanType.CustomUIs, Path.Combine(folderPath, "src", "Components", "CustomUIs")),
            (ScanType.CustomUIs, Path.Combine(folderPath, "src", "components", "CustomUIs")),
            // (2) New DesignSystem convention (repo-root -> src/DesignSystem/components/)
            (ScanType.DesignSystem, Path.Combine(folderPath, "src", "DesignSystem", "components")),
            (ScanType.DesignSystem, Path.Combine(folderPath, "src", "DesignSystem", "Components")),
            (ScanType.DesignSystem, Path.Combine(folderPath, "src", "designSystem", "components")),
            // (3) Direct DesignSystem path (--components-source points AT DesignSystem/)
            (ScanType.DesignSystem, Path.Combine(folderPath, "components")),
            (ScanType.DesignSystem, Path.Combine(folderPath, "Components")),
            // (4) Direct CustomUIs path (--components-source points AT CustomUIs/)
            (ScanType.CustomUIs, Path.Combine(folderPath, "CustomUIs")),
            // (5) DesignSystem folder itself contains component subfolders
            //     (e.g. --components-source points at src/DesignSystem which
            //      has Button/, Input/, etc. directly — no nested "components/" subdir)
            (ScanType.DesignSystem, folderPath),
            // (6) Plain Components/ fallback (repo-root -> src/Components/)
            (ScanType.Plain, Path.Combine(folderPath, "src", "Components")),
            (ScanType.Plain, Path.Combine(folderPath, "src", "components")),
            // (7) Last resort: folderPath itself is the components root
            (ScanType.Plain, folderPath),
        };

        // DesignSystem probes (3) and (5) could match the same folder
        // (e.g. src/DesignSystem/components/ exists AND folderPath itself
        // has component subdirs).  We want the more-specific nested path
        // first, so we deduplicate: if we've already scanned this exact
        // physical path, skip subsequent duplicates.
        var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (scanType, path) in probePaths)
        {
            if (!Directory.Exists(path))
                continue;

            if (!scannedPaths.Add(path))
                continue; // already scanned this exact path

            if (scanType == ScanType.DesignSystem)
            {
                ScanComponentFolderFlat(path, results);
            }
            else
            {
                ScanComponentFolder(path, results);
            }

            break; // first hit wins
        }

        foreach (var (scanType, path) in probePaths)
        {
            if (!Directory.Exists(path))
                continue;

            if (scanType == ScanType.DesignSystem)
            {
                // DesignSystem/components/ is flat: each subfolder is a
                // component.  Scan one level (no nested sub-components).
                ScanComponentFolderFlat(path, results);
            }
            else
            {
                ScanComponentFolder(path, results);
            }

            break; // first hit wins
        }

        // Defense in depth against case-sensitive file systems or any
        // remaining path overlap: de-dupe by physical path before filtering.
        return results
            .GroupBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Where(r => HasComponentExport(r.Path))
            .ToList();
    }

    private enum ScanType { CustomUIs, DesignSystem, Plain }

    /// <summary>
    /// Scans a flat component folder (one folder per component, no nested
    /// sub-components). This is the DesignSystem convention.
    /// </summary>
    private static void ScanComponentFolderFlat(string rootPath, List<(string Path, bool IsNested)> results)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(rootPath))
            {
                var files = Directory.GetFiles(dir, "*.tsx")
                    .Concat(Directory.GetFiles(dir, "*.jsx"))
                    .Concat(Directory.GetFiles(dir, "*.js"));
                results.AddRange(files.Select(f => (f, false)));
            }

            // Also include loose files at the root level
            results.AddRange(Directory.GetFiles(rootPath, "*.tsx").Select(f => (f, false)));
            results.AddRange(Directory.GetFiles(rootPath, "*.jsx").Select(f => (f, false)));
            results.AddRange(Directory.GetFiles(rootPath, "*.js").Select(f => (f, false)));
        }
        catch { /* skip inaccessible directories */ }
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

        // Extract props: prefer a rich TypeScript interface parse (DesignSystem
        // convention) that captures enum values, defaults, descriptions, and
        // inherited HTMLAttributes props; fall back to PropTypes.* (BUS core
        // plain JS/JSX convention).
        var unions = ExtractTypeUnions(content);
        var defaults = ExtractDefaultsFromSignature(content, componentName);
        List<ComponentProp> props;
        if (InterfaceHeaderRegex.IsMatch(content))
            props = ExtractInterfacePropsRich(content, unions, defaults);
        else
            props = ExtractPropTypesProps(content);

        // Variants: derive from `export type XVariant = 'a' | 'b'` unions.
        var variants = ExtractVariants(content);

        // Tokens consumed: read the co-located SCSS sidecar's `// token:` and
        // `$bus-ds-*` references (DesignSystem convention), falling back to a
        // content heuristic for legacy components without a sidecar.
        var tokensConsumed = ExtractTokensFromScssSidecar(filePath, content, componentId);

        // Accessibility: infer role / aria-* / keyboard support from the JSX.
        var accessibility = ExtractAccessibility(content);

        var importPath = ComputeImportPath(filePath);
        var relativeSource = Path.GetRelativePath(Path.GetDirectoryName(filePath)!, filePath);
        var description = ExtractLeadingDescription(content, componentName, relativeSource);

        return new ComponentDetail
        {
            ComponentId = componentId,
            Name = ToHumanName(componentName),
            Category = category,
            Status = "approved",
            Description = description,
            Props = props,
            Variants = variants.Count > 0 ? variants : null,
            TokensConsumed = tokensConsumed,
            Examples = null,
            Accessibility = accessibility,
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
    /// <summary>
    /// Computes an import path relative to the project's <c>src/</c> folder.
    /// If the component folder has an <c>index.ts</c> (or <c>.js</c>) barrel
    /// that re-exports the target file, we return the folder path (e.g.
    /// <c>DesignSystem/components/Tabs</c>). Otherwise we return the file
    /// path without extension (e.g.
    /// <c>DesignSystem/components/Tabs/Tabs</c>).
    /// </summary>
    private static string ComputeImportPath(string filePath)
    {
        var normalized = filePath.Replace('\\', '/');
        var srcIndex = normalized.LastIndexOf("/src/", StringComparison.OrdinalIgnoreCase);
        var relative = srcIndex >= 0 ? normalized[(srcIndex + 5)..] : Path.GetFileName(normalized);
        var withoutExt = Path.ChangeExtension(relative, null) ?? relative;

        // If the component folder has a barrel file (index.ts / index.js)
        // that re-exports the target, the import should be the folder path,
        // not the file path.
        var dir = Path.GetDirectoryName(filePath);
        if (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "index.ts")) ||
                File.Exists(Path.Combine(dir, "index.js")) ||
                File.Exists(Path.Combine(dir, "index.tsx")) ||
                File.Exists(Path.Combine(dir, "index.jsx")))
            {
                // Verify the barrel actually re-exports the target file
                var barrelPath = Path.Combine(dir, "index.ts");
                if (!File.Exists(barrelPath)) barrelPath = Path.Combine(dir, "index.js");
                if (!File.Exists(barrelPath)) barrelPath = Path.Combine(dir, "index.tsx");
                if (!File.Exists(barrelPath)) barrelPath = Path.Combine(dir, "index.jsx");

                if (File.Exists(barrelPath))
                {
                    var barrelContent = File.ReadAllText(barrelPath);
                    var targetName = Path.GetFileNameWithoutExtension(filePath);
                    if (barrelContent.Contains(targetName))
                    {
                        // Use folder path (one segment shorter)
                        var folderPath = Path.GetDirectoryName(withoutExt.Replace('/', Path.DirectorySeparatorChar))?.Replace('\\', '/');
                        return folderPath ?? withoutExt;
                    }
                }
            }
        }

        return withoutExt;
    }

    private static string ClassifyComponent(string componentId, string folderName, string content)
    {
        var lower = componentId.ToLowerInvariant();
        var folder = folderName.ToLowerInvariant();
        var contentLower = content.ToLowerInvariant();

        // NOTE: categories must stay within the component-catalog schema enum:
        // button, input, select, table, dialog, layout, navigation, card,
        // typography, feedback, other. (Legacy "form"/"display" were invalid.)
        if ((lower.Contains("button") && !lower.Contains("radio")) || folder.Contains("button")) return "button";
        if (lower.Contains("grid") || lower.Contains("datatable") || lower.Contains("table")
            || folder.Contains("table") || folder.Contains("grid")) return "table";
        if (lower.Contains("tab") || lower.Contains("pagination") || folder.Contains("tab")) return "navigation";
        if (lower.Contains("dropdown") || lower.Contains("menu")) return "select";
        if (lower.Contains("select")) return "select";
        if (lower.Contains("search")) return "input";
        if (lower.Contains("input") || lower.Contains("textbox") || folder.Contains("input")) return "input";
        if (lower.Contains("checkbox") || lower.Contains("check")) return "input";
        if (lower.Contains("switch") || lower.Contains("toggle")) return "input";
        if (lower.Contains("form") || lower.Contains("field") || folder.Contains("form")) return "input";
        if (lower.Contains("chips") || lower.Contains("chip")) return "input";
        if (lower.Contains("statuspill") || lower.Contains("pill") || lower.Contains("badge")
            || lower.Contains("label")) return "feedback";
        if (lower.Contains("card")) return "card";
        if (lower.Contains("shell") || lower.Contains("layout") || lower.Contains("header")
            || lower.Contains("sidenav") || lower.Contains("container")) return "layout";
        if (lower.Contains("dialog") || lower.Contains("modal") || lower.Contains("window")
            || lower.Contains("popup") || folder.Contains("dialog")) return "dialog";
        if (lower.Contains("skeleton") || lower.Contains("progress") || lower.Contains("loading")
            || lower.Contains("spinner")) return "feedback";

        return "other";
    }

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

        if (lower.Contains("button") && !lower.Contains("radio")) maps.Add("button");
        if (lower.Contains("grid") || lower.Contains("datatable") || lower.EndsWith("table")) maps.Add("table");
        if (lower.Contains("tab")) maps.AddRange(new[] { "tabs", "tab" });
        if (lower.Contains("pagination")) maps.Add("nav");
        if (lower.Contains("search")) maps.AddRange(new[] { "search", "input" });
        if (lower.Contains("input") || lower.Contains("textbox")) maps.Add("input");
        if (lower.Contains("select") || lower.Contains("dropdown")) maps.Add("select");
        if (lower.Contains("checkbox")) maps.Add("checkbox");
        if (lower.Contains("switch") || lower.Contains("toggle")) maps.Add("switch");
        if (lower.Contains("statuspill") || lower.Contains("pill") || lower.Contains("badge")) maps.AddRange(new[] { "badge", "span" });
        if (lower.Contains("chips") || lower.Contains("chip")) maps.AddRange(new[] { "chip", "input" });
        if (lower.Contains("form") || lower.Contains("field")) maps.Add("form");
        if (lower.Contains("card")) maps.Add("card");
        if (lower.Contains("header") && !lower.Contains("workflow")) maps.Add("header");
        if (lower.Contains("sidenav") || lower.Contains("navbar") || lower.Contains("navigation")) maps.Add("nav");
        if (lower.Contains("shell") || lower.Contains("container")) maps.Add("div");

        return maps.Distinct().ToList();
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
            layouts = ExistingFilesInDir("layouts", "*.json"),
            referenceUiPatterns = ExistingFilesInDir("referenceUiPatterns", "*.json"),
            icons = ExistingKnowledgeFileOrDefault("icons/icons.json"),
            bestPractices = ExistingFilesInDir("best-practices", "*.md"),
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

    // ── Trainer v2 helper methods ──────────────────────────────────────────

    /// <summary>
    /// Builds a semantic description from the leading <c>//</c> comment block
    /// above the first import/export/interface. Falls back to a generated
    /// "<c>{componentName} component</c>" string when no header comment exists
    /// (legacy plain-JS components). This replaces the uninformative
    /// "Extracted from X.js" placeholder so <c>match_element</c> and the
    /// generator get real signal.
    /// </summary>
    private static string ExtractLeadingDescription(string content, string componentName, string sourceFile)
    {
        var firstCodeLine = FirstCodeLineRegex.Match(content);
        if (!firstCodeLine.Success)
            return $"{componentName} component (source: {sourceFile}).";

        var header = content.Substring(0, firstCodeLine.Index);
        var lines = header.Split('\n')
            .Select(l => l.Trim().TrimStart('/').Trim())
            .Where(l => l.Length > 0)
            .ToList();

        if (lines.Count == 0)
            return $"{componentName} component (source: {sourceFile}).";

        var desc = lines[0];
        var tokensLine = lines.FirstOrDefault(l => l.StartsWith("Tokens:", StringComparison.OrdinalIgnoreCase));
        if (tokensLine is not null)
            desc += ". " + tokensLine;
        return desc;
    }

    /// <summary>Parses all <c>export type X = 'a' | 'b';</c> unions into a map.</summary>
    private static Dictionary<string, List<string>> ExtractTypeUnions(string content)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (Match m in TypeUnionRegex.Matches(content))
        {
            var name = m.Groups[1].Value.Trim();
            var body = m.Groups[2].Value;
            var values = StringLiteralRegex.Matches(body)
                .Cast<Match>()
                .Select(x => x.Groups[1].Value)
                .ToList();
            if (values.Count > 0)
                map[name] = values;
        }
        return map;
    }

    /// <summary>
    /// Extracts prop default values from the function destructure, e.g.
    /// <c>function Button({ variant = 'primary', loading = false, ... })</c>.
    /// </summary>
    private static Dictionary<string, object?> ExtractDefaultsFromSignature(string content, string componentName)
    {
        var defaults = new Dictionary<string, object?>(StringComparer.Ordinal);
        // Match `function Button({ a = 'x', b = 2, ...rest }: ButtonProps)` — note
        // the `(` before the destructure `{`, which the original regex missed.
        var sigRegex = new Regex(
            $@"function\s+{Regex.Escape(componentName)}\s*\(\s*\{{([^}}]*)\}}",
            RegexOptions.Compiled);
        var m = sigRegex.Match(content);
        if (!m.Success)
            return defaults;

        var destructure = m.Groups[1].Value;
        foreach (Match dm in DestructureDefaultRegex.Matches(destructure))
        {
            var propName = dm.Groups[1].Value;
            var rawVal = dm.Groups[2].Value;
            object? value;
            if (dm.Groups[3].Success)
                value = dm.Groups[3].Value;               // 'string'
            else if (dm.Groups[4].Success)
                value = dm.Groups[4].Value;                // "string"
            else if (rawVal == "true")
                value = true;
            else if (rawVal == "false")
                value = false;
            else if (int.TryParse(rawVal, out var iv))
                value = iv;
            else if (double.TryParse(rawVal, System.Globalization.NumberStyles.Any,
                         System.Globalization.CultureInfo.InvariantCulture, out var dv))
                value = dv;
            else
                value = rawVal;
            defaults[propName] = value;
        }
        return defaults;
    }

    /// <summary>
    /// Rich TS prop extraction: captures enum values (from type unions),
    /// default values (from the destructure), per-prop descriptions, and
    /// inherited HTMLAttributes props (resolving the <c>{...rest}</c> spread
    /// that the old PropTypes-only extractor missed).
    /// </summary>
    private static List<ComponentProp> ExtractInterfacePropsRich(
        string content,
        IReadOnlyDictionary<string, List<string>> unions,
        IReadOnlyDictionary<string, object?> defaults)
    {
        var props = new List<ComponentProp>();
        var header = InterfaceHeaderRegex.Match(content);
        if (!header.Success)
            return props;

        var extendsClause = header.Groups[2].Success ? header.Groups[2].Value.Trim() : null;
        var bodyOpen = header.Index + header.Length - 1; // index of '{'
        var bodyClose = FindMatchingBrace(content, bodyOpen);
        if (bodyClose <= bodyOpen)
            return props;
        var body = content.Substring(bodyOpen + 1, bodyClose - bodyOpen - 1);

        foreach (var stmt in SplitTopLevel(body, ';'))
        {
            var trimmed = stmt.Trim();
            if (trimmed.Length == 0
                || trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith("/*", StringComparison.Ordinal))
                continue;

            var (comment, propPart) = SplitComment(trimmed);
            var pm = PropDeclRegex.Match(propPart.Trim());
            if (!pm.Success)
                continue;

            var propName = pm.Groups[1].Value;
            var required = !pm.Groups[2].Success; // group 2 = optional '?'
            var rawType = pm.Groups[3].Value.Trim().TrimEnd(';').Trim();
            var type = MapTypeRich(rawType);

            var prop = new ComponentProp
            {
                Name = propName,
                Type = type,
                Required = required,
                Description = string.IsNullOrWhiteSpace(comment) ? null : comment
            };

            // Enum: resolve named union, or inline 'a' | 'b' union.
            if (unions.TryGetValue(rawType, out var unionValues))
                prop = prop with { Enum = unionValues };
            else if (rawType.StartsWith("'") && rawType.Contains('|'))
            {
                var inline = StringLiteralRegex.Matches(rawType)
                    .Cast<Match>().Select(x => x.Groups[1].Value).ToList();
                if (inline.Count > 0)
                    prop = prop with { Enum = inline };
            }

            // Default from the destructure map.
            if (defaults.TryGetValue(propName, out var def))
                prop = prop with { Default = def };

            props.Add(prop);
        }

        if (!string.IsNullOrEmpty(extendsClause) && extendsClause.Contains("HTMLAttributes"))
            props = MergeInheritedHtmlProps(props);

        return props;
    }

    /// <summary>Splits a trailing <c>// comment</c> off a prop statement.</summary>
    private static (string? comment, string propPart) SplitComment(string stmt)
    {
        var idx = stmt.IndexOf("//", StringComparison.Ordinal);
        if (idx < 0)
            return (null, stmt);
        return (stmt.Substring(idx + 2).Trim(), stmt.Substring(0, idx));
    }

    private static string MapTypeRich(string tsType)
    {
        var t = tsType.Trim();
        if (t.EndsWith("[]"))
            t = t.Substring(0, t.Length - 2).Trim();
        var lower = t.ToLowerInvariant();
        return lower switch
        {
            "string" => "string",
            "number" => "number",
            "boolean" => "boolean",
            "reactnode" or "react.reactnode" => "ReactNode",
            "function" or "void" => "function",
            "any" => "any",
            "object" => "object",
            _ when lower.Contains("|") => "string",      // union of string literals
            _ when lower.StartsWith("(") => "function",  // function type literal
            _ => t                                       // keep named types (e.g. ButtonVariant)
        };
    }

    /// <summary>Returns the index of the brace matching the <c>{</c> at <paramref name="openIndex"/>.</summary>
    private static int FindMatchingBrace(string content, int openIndex)
    {
        if (openIndex < 0 || openIndex >= content.Length || content[openIndex] != '{')
            return -1;
        var depth = 0;
        for (var i = openIndex; i < content.Length; i++)
        {
            var c = content[i];
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Adds common props inherited from React.HTMLAttributes / *HTMLAttributes
    /// (the <c>{...rest}</c> spread) that aren't already explicitly declared.
    /// </summary>
    private static List<ComponentProp> MergeInheritedHtmlProps(List<ComponentProp> props)
    {
        var inherited = new (string name, string type)[]
        {
            ("onClick", "function"), ("onChange", "function"), ("onFocus", "function"),
            ("onBlur", "function"), ("onKeyDown", "function"), ("disabled", "boolean"),
            ("id", "string"), ("className", "string"), ("style", "object"), ("type", "string"),
            ("name", "string"), ("value", "string"), ("placeholder", "string"),
            ("readOnly", "boolean"), ("autoFocus", "boolean"), ("tabIndex", "number")
        };
        var existing = props.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var (name, type) in inherited)
        {
            if (!existing.Contains(name))
                props.Add(new ComponentProp { Name = name, Type = type, Required = false });
        }
        return props;
    }

    /// <summary>
    /// Derives named variants from every <c>export type XVariant/YSize = ...</c>
    /// union in the file, so the generator knows the exact allowed values
    /// instead of inventing ones like <c>variant="danger"</c>.
    /// </summary>
    private static List<ComponentVariant> ExtractVariants(string content)
    {
        var variants = new List<ComponentVariant>();
        foreach (var (unionName, values) in ExtractTypeUnions(content))
        {
            foreach (var value in values)
            {
                variants.Add(new ComponentVariant
                {
                    Name = value,
                    Description = $"{unionName} = '{value}'"
                });
            }
        }
        return variants;
    }

    /// <summary>Infers accessibility metadata (role, aria-*, keyboard) from the JSX.</summary>
    private static ComponentAccessibility? ExtractAccessibility(string content)
    {
        var ariaProps = AriaAttrRegex.Matches(content)
            .Cast<Match>()
            .Select(m => "aria-" + m.Groups[1].Value)
            .Distinct()
            .ToList();
        var roleMatch = RoleAttrRegex.Match(content);
        var hasKeyboard = content.Contains("onKeyDown") || content.Contains("onKeyPress")
            || content.Contains("onKeyUp") || content.Contains("focus-visible")
            || content.Contains("tabIndex");

        if (ariaProps.Count == 0 && !roleMatch.Success && !hasKeyboard)
            return null;

        return new ComponentAccessibility
        {
            Role = roleMatch.Success ? roleMatch.Groups[1].Value : null,
            KeyboardSupport = hasKeyboard ? true : null,
            AriaProps = ariaProps.Count > 0 ? ariaProps : null,
            Notes = null
        };
    }

    /// <summary>
    /// Reads the co-located SCSS sidecar's <c>// token:</c> comments and
    /// <c>$bus-ds-*</c> references to record the real tokens a component
    /// consumes (DesignSystem convention). Falls back to the legacy content
    /// heuristic when no sidecar exists.
    /// </summary>
    private static List<string> ExtractTokensFromScssSidecar(string tsxPath, string content, string componentId)
    {
        var tokens = new List<string>();
        var dir = Path.GetDirectoryName(tsxPath);
        var baseName = Path.GetFileNameWithoutExtension(tsxPath);
        if (dir is not null)
        {
            var scssPath = Path.Combine(dir, baseName + ".scss");
            if (File.Exists(scssPath))
            {
                var scss = File.ReadAllText(scssPath);
                foreach (Match m in TokenCommentRegex.Matches(scss))
                    tokens.Add(m.Groups[1].Value);
                foreach (Match m in ScssVarRefRegex.Matches(scss))
                    tokens.Add("$" + m.Groups[1].Value);
            }
        }

        if (tokens.Count == 0)
            tokens.AddRange(InferTokensConsumed(content, componentId));

        return tokens.Distinct().ToList();
    }

    /// <summary>
    /// Scans <c>src/DesignSystem/layout/*/</c> for layout components (AppShell,
    /// Header, PageHeader, SideNav) and builds LayoutPattern entries with slots
    /// inferred from their props/JSX. Fills the previously single-entry layouts KB.
    /// </summary>
    private List<LayoutPattern> ExtractLayouts(string folderPath)
    {
        var layouts = new List<LayoutPattern>();
        var candidates = new[]
        {
            Path.Combine(folderPath, "src", "DesignSystem", "layout"),
            Path.Combine(folderPath, "src", "DesignSystem", "Layout"),
            Path.Combine(folderPath, "layout"),
            Path.Combine(folderPath, "Layout"),
        };
        var layoutDir = candidates.FirstOrDefault(Directory.Exists);
        if (layoutDir is null)
            return layouts;

        foreach (var dir in Directory.GetDirectories(layoutDir))
        {
            var tsx = Directory.GetFiles(dir, "*.tsx")
                .Concat(Directory.GetFiles(dir, "*.jsx"))
                .FirstOrDefault();
            if (tsx is null)
                continue;

            var componentContent = File.ReadAllText(tsx);
            var name = Path.GetFileNameWithoutExtension(tsx);
            layouts.Add(new LayoutPattern
            {
                LayoutId = name,
                Name = name,
                Description = ExtractLeadingDescription(componentContent, name, tsx),
                Slots = InferLayoutSlots(componentContent)
            });
        }
        return layouts;
    }

    private static List<string> InferLayoutSlots(string content)
    {
        var slots = new List<string>();
        var lower = content.ToLowerInvariant();

        if (content.Contains("children")) slots.Add("children");
        if (content.Contains("headerProps") || lower.Contains("header")) slots.Add("header");
        if (content.Contains("navItems") || content.Contains("navGroups")
            || lower.Contains("sidenav") || lower.Contains("sidebar"))
            slots.Add("sidebar");
        if (lower.Contains("main") || content.Contains("contentClassName")) slots.Add("main");
        if (lower.Contains("footer")) slots.Add("footer");
        if (content.Contains("actions")) slots.Add("actions");
        if ((content.Contains("title") || content.Contains("subtitle")) && !slots.Contains("header"))
            slots.Add("header");

        return slots.Distinct().ToList();
    }

    private void WriteLayoutFile(LayoutPattern layout)
    {
        var dir = Path.Combine(_knowledgePath, "layouts");
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, $"{layout.LayoutId}.json");
        var json = JsonConvert.SerializeObject(layout, new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        });
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Lists every file matching <paramref name="pattern"/> under a knowledge
    /// subfolder, as relative manifest paths. Auto-discovers trained AND
    /// pre-seeded assets (layouts, referenceUiPatterns, best-practices)
    /// instead of hardcoding single filenames.
    /// </summary>
    private string[] ExistingFilesInDir(string dirRelative, string pattern)
    {
        var dir = Path.Combine(_knowledgePath, dirRelative);
        if (!Directory.Exists(dir))
            return Array.Empty<string>();
        return Directory.GetFiles(dir, pattern)
            .Select(f => $"{dirRelative}/{Path.GetFileName(f)}")
            .OrderBy(x => x)
            .ToArray();
    }
}
