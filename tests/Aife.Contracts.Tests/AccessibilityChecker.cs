using System.Text.RegularExpressions;

namespace Aife.Contracts.Tests;

/// <summary>
/// Static accessibility checker that scans generated React/TSX code for common
/// accessibility issues. This is a supplement to the AI Reviewer; it runs as a
/// contract test so regressions are visible (per testing strategy E8-2).
/// </summary>
public static class AccessibilityChecker
{
    public static IReadOnlyList<AccessibilityFinding> Check(string code)
    {
        var findings = new List<AccessibilityFinding>();

        CheckInputLabels(code, findings);
        CheckButtonAriaLabels(code, findings);
        CheckTableHeaderScope(code, findings);

        return findings;
    }

    /// <summary>
    /// Inputs should have an associated label (aria-label, aria-labelledby, or id matching a label).
    /// </summary>
    private static void CheckInputLabels(string code, List<AccessibilityFinding> findings)
    {
        var inputRegex = new Regex(@"<input\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        foreach (Match match in inputRegex.Matches(code))
        {
            var tag = match.Value;
            if (!tag.Contains("aria-label", StringComparison.OrdinalIgnoreCase) &&
                !tag.Contains("aria-labelledby", StringComparison.OrdinalIgnoreCase) &&
                !tag.Contains("id=", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new AccessibilityFinding(
                    "A11Y_INPUT_LABEL",
                    "Input element without an associated label, aria-label, or id.",
                    match.Index));
            }
        }
    }

    /// <summary>
    /// Buttons should have text content or an aria-label.
    /// </summary>
    private static void CheckButtonAriaLabels(string code, List<AccessibilityFinding> findings)
    {
        var buttonRegex = new Regex(@"<button\b[^>]*>(.*?)</button>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match match in buttonRegex.Matches(code))
        {
            var openingTag = match.Groups[0].Value.Substring(0, match.Groups[0].Value.IndexOf('>') + 1);
            var innerText = match.Groups[1].Value.Trim();

            if (string.IsNullOrWhiteSpace(innerText) &&
                !openingTag.Contains("aria-label", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new AccessibilityFinding(
                    "A11Y_BUTTON_LABEL",
                    "Button element without text content or aria-label.",
                    match.Index));
            }
        }
    }

    /// <summary>
    /// Table header cells should declare scope.
    /// </summary>
    private static void CheckTableHeaderScope(string code, List<AccessibilityFinding> findings)
    {
        var thRegex = new Regex(@"<th\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        foreach (Match match in thRegex.Matches(code))
        {
            if (!match.Value.Contains("scope", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new AccessibilityFinding(
                    "A11Y_TABLE_HEADER_SCOPE",
                    "Table header cell (<th>) without scope attribute.",
                    match.Index));
            }
        }
    }
}

public sealed record AccessibilityFinding(string RuleId, string Message, int Position);
