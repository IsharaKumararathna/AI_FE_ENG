using System.Text.RegularExpressions;
using Aife.Application.Knowledge;

namespace Aife.Knowledge;

public sealed record TokenViolation
{
    public required string RuleId { get; init; }
    public required string Value { get; init; }
    public required string Message { get; init; }
    public string? SuggestedToken { get; init; }
    public string? Location { get; init; }
}

public sealed record TokenConformanceResult
{
    public required IReadOnlyList<TokenViolation> Violations { get; init; }

    /// <summary>0-100 sub-score: 100 minus a penalty per violation, floored at 0.</summary>
    public required double Score { get; init; }
}

/// <summary>
/// Deterministic (regex-based, no LLM) check for hardcoded colors/spacing
/// values that bypass the design token palette. Reuses the same detection
/// logic as <c>PrototypeConformanceReviewer.CheckHardcodedColorsAsync</c> in
/// Aife.Ai so the legacy HTTP pipeline and the MCP <c>check_token_conformance</c>
/// tool report identical findings.
/// </summary>
public sealed class TokenConformanceChecker
{
    private static readonly Regex HexColorRegex =
        new(@"#(?:[0-9a-fA-F]{3}){1,2}\b", RegexOptions.Compiled);

    private static readonly Regex RgbColorRegex =
        new(@"rgba?\(\s*\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PxSpacingRegex =
        new(@"(?:margin|padding|gap)(?:-(?:top|right|bottom|left))?\s*:\s*(\d+px)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IKnowledgeProvider _knowledgeProvider;

    public TokenConformanceChecker(IKnowledgeProvider knowledgeProvider)
    {
        _knowledgeProvider = knowledgeProvider;
    }

    public async Task<TokenConformanceResult> CheckAsync(string css, CancellationToken ct)
    {
        var tokens = await _knowledgeProvider.GetDesignTokensAsync(ct);
        var tokenColors = tokens.Tokens
            .Where(t => t.Category.Equals("color", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tokenSpacing = tokens.Tokens
            .Where(t => t.Category.Equals("spacing", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var violations = new List<TokenViolation>();

        foreach (Match match in HexColorRegex.Matches(css))
        {
            var color = match.Value;
            if (tokenColors.Contains(color))
                continue;

            violations.Add(new TokenViolation
            {
                RuleId = "CONF_COLOR_OFF_TOKEN",
                Value = color,
                Message = $"Hardcoded color '{color}' is not in the design token palette.",
                SuggestedToken = FindNearestColorToken(color, tokens),
                Location = $"Position {match.Index}"
            });
        }

        foreach (Match match in RgbColorRegex.Matches(css))
        {
            violations.Add(new TokenViolation
            {
                RuleId = "CONF_RGB_COLOR",
                Value = match.Value,
                Message = "RGB/RGBA color literal found. Use design tokens only.",
                Location = $"Position {match.Index}"
            });
        }

        foreach (Match match in PxSpacingRegex.Matches(css))
        {
            var value = match.Groups[1].Value;
            if (tokenSpacing.Contains(value))
                continue;

            violations.Add(new TokenViolation
            {
                RuleId = "CONF_SPACING_OFF_TOKEN",
                Value = value,
                Message = $"Hardcoded spacing '{value}' is not in the design token scale.",
                SuggestedToken = FindNearestSpacingToken(value, tokens),
                Location = $"Position {match.Index}"
            });
        }

        // Simple MVP penalty model: -10 per violation, floored at 0.
        var score = Math.Max(0, 100 - (violations.Count * 10));

        return new TokenConformanceResult { Violations = violations, Score = score };
    }

    private static string? FindNearestColorToken(string color, DesignTokenSet tokens) =>
        tokens.Tokens
            .Where(t => t.Category.Equals("color", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Name)
            .FirstOrDefault();

    private static string? FindNearestSpacingToken(string pxValue, DesignTokenSet tokens)
    {
        if (!int.TryParse(pxValue.Replace("px", ""), out var px))
            return null;

        return tokens.Tokens
            .Where(t => t.Category.Equals("spacing", StringComparison.OrdinalIgnoreCase)
                && t.Value.EndsWith("px", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(t.Value.Replace("px", ""), out _))
            .OrderBy(t => Math.Abs(int.Parse(t.Value.Replace("px", "")) - px))
            .Select(t => t.Name)
            .FirstOrDefault();
    }
}
