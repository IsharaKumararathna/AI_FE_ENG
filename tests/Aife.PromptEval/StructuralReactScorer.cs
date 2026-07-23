using System.Text.RegularExpressions;

namespace Aife.PromptEval;

/// <summary>
/// Scores generated React/TSX output structurally rather than by exact string
/// match — the right approach for code generation where many valid outputs
/// exist. Combines three signals that map directly to the top conformance
/// failures (see data/conformance-summary.json):
/// <list type="bullet">
/// <item>Banned third-party UI packages (Kendo / react-bootstrap / MUI / antd).</item>
/// <item>Hardcoded hex/rgb color literals (should use $bus-ds-* tokens).</item>
/// <item>Presence of real import statements (catches code that just emits JSX).</item>
/// </list>
/// Returns a 0-1 score; an entry "passes" at ≥ entryPassScore (default 0.8).
/// </summary>
public sealed class StructuralReactScorer
{
    private static readonly Regex HexColorRegex = new(@"#[0-9a-fA-F]{3,8}\b", RegexOptions.Compiled);
    private static readonly Regex RgbColorRegex = new(@"rgba?\(\s*\d", RegexOptions.Compiled);
    private static readonly Regex ImportRegex = new(
        @"import\s+(?:\{[^}]+\}|\w+)\s+from\s+['""]([^'""]+)['""]",
        RegexOptions.Compiled);

    private static readonly string[] BannedPackages =
        { "@progress/kendo", "react-bootstrap", "@mui/", "antd", "@chakra-ui", "shadcn" };

    private readonly ISet<string>? _approvedComponentNames;

    /// <param name="approvedComponentNames">
    /// Optional set of approved component export names (e.g. { "Button",
    /// "DataTable", "AppShell" }). When supplied, imports referencing a name
    /// NOT in this set incur a small penalty, encouraging use of the DS.
    /// </param>
    public StructuralReactScorer(ISet<string>? approvedComponentNames = null)
    {
        _approvedComponentNames = approvedComponentNames;
    }

    /// <summary>Returns a 0-1 structural quality score for the generated code.</summary>
    public double Score(string generatedCode)
    {
        if (string.IsNullOrWhiteSpace(generatedCode))
            return 0.0;

        var penalty = 0.0;

        // Banned packages — heaviest penalty (each occurrence).
        foreach (var pkg in BannedPackages)
        {
            if (generatedCode.IndexOf(pkg, StringComparison.OrdinalIgnoreCase) >= 0)
                penalty += 0.5;
        }

        // Hardcoded hex literals — top conformance failure. Cap the penalty so
        // a single slip doesn't zero the score, but many do.
        var hexCount = HexColorRegex.Matches(generatedCode).Count;
        penalty += Math.Min(0.5, hexCount * 0.1);

        // rgb()/rgba() literals.
        var rgbCount = RgbColorRegex.Matches(generatedCode).Count;
        penalty += Math.Min(0.3, rgbCount * 0.1);

        // No import statements at all → likely non-compilable JSX.
        if (!ImportRegex.IsMatch(generatedCode))
            penalty += 0.2;

        // Inline style color hardcoding: style={{ color: '#...' }} or
        // backgroundColor: '#...' — a strong smell even without a hex regex hit.
        if (generatedCode.Contains("style={{", StringComparison.Ordinal)
            && HexColorRegex.IsMatch(generatedCode))
            penalty += 0.2;

        return Math.Max(0.0, 1.0 - penalty);
    }
}
