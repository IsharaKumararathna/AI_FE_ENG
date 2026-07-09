using FluentAssertions;
using Xunit;

namespace Aife.Contracts.Tests;

public class ConformanceReportSchemaTests
{
    private static readonly string SchemaPath =
        Path.Combine(TestPaths.Schemas, "prototype-conformance-report.schema.json");

    private const string SampleReport = """
    {
      "prototypeId": "p-001",
      "outcome": "Passed with warnings",
      "findings": [
        {
          "ruleId": "CONF_COLOR_OFF_TOKEN",
          "category": "Token conformance",
          "severity": "advisory",
          "message": "Background #123456 is not in the color token palette; nearest token is color.surface.muted.",
          "location": { "selector": ".hero", "property": "background-color" }
        }
      ],
      "suggestions": [
        "Replace #123456 with color.surface.muted to match the current dashboard hero."
      ]
    }
    """;

    [Fact]
    public void Sample_conformance_report_validates_against_schema()
    {
        var (isValid, errors) = SchemaValidator.Validate(SchemaPath, SampleReport);

        isValid.Should().BeTrue(
            "The sample PrototypeConformanceReport must conform to the conformance report schema. Errors:\n{0}", errors);
    }
}
