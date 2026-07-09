using FluentAssertions;
using Xunit;

namespace Aife.Contracts.Tests;

public class AccessibilityCheckerTests
{
    [Fact]
    public void Input_without_label_produces_finding()
    {
        var code = """<input type="text" name="username" />""";

        var findings = AccessibilityChecker.Check(code);

        findings.Should().Contain(f => f.RuleId == "A11Y_INPUT_LABEL");
    }

    [Fact]
    public void Input_with_aria_label_produces_no_finding()
    {
        var code = """<input type="text" aria-label="Username" />""";

        var findings = AccessibilityChecker.Check(code);

        findings.Should().NotContain(f => f.RuleId == "A11Y_INPUT_LABEL");
    }

    [Fact]
    public void Button_without_text_or_aria_label_produces_finding()
    {
        var code = """<button onClick={handleClick}></button>""";

        var findings = AccessibilityChecker.Check(code);

        findings.Should().Contain(f => f.RuleId == "A11Y_BUTTON_LABEL");
    }

    [Fact]
    public void Button_with_text_produces_no_finding()
    {
        var code = """<button onClick={handleClick}>Submit</button>""";

        var findings = AccessibilityChecker.Check(code);

        findings.Should().NotContain(f => f.RuleId == "A11Y_BUTTON_LABEL");
    }

    [Fact]
    public void Table_header_without_scope_produces_finding()
    {
        var code = """<th>Name</th>""";

        var findings = AccessibilityChecker.Check(code);

        findings.Should().Contain(f => f.RuleId == "A11Y_TABLE_HEADER_SCOPE");
    }

    [Fact]
    public void Table_header_with_scope_produces_no_finding()
    {
        var code = """<th scope="col">Name</th>""";

        var findings = AccessibilityChecker.Check(code);

        findings.Should().NotContain(f => f.RuleId == "A11Y_TABLE_HEADER_SCOPE");
    }

    [Fact]
    public void Fully_accessible_code_produces_no_findings()
    {
        var code = """
            <input type="text" aria-label="Search" />
            <button onClick={search}>Search</button>
            <th scope="col">Name</th>
            """;

        var findings = AccessibilityChecker.Check(code);

        findings.Should().BeEmpty();
    }
}
