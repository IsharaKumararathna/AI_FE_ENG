using FluentAssertions;
using Xunit;

namespace Aife.Contracts.Tests;

public class IntermediateUiSchemaTests
{
    private static readonly string SchemaPath =
        Path.Combine(TestPaths.Schemas, "intermediate-ui.schema.json");

    private const string SampleTree = """
    {
      "page": "Dashboard",
      "layout": "AppLayout",
      "children": [
        {
          "nodeId": "n1",
          "componentId": "PageHeader",
          "props": { "title": "Dashboard" },
          "tokenBindings": { "titleColor": "color.text.primary" }
        },
        {
          "nodeId": "n2",
          "componentId": "DataTable",
          "variant": "default",
          "props": {
            "columns": ["Name", "Status", "Updated"],
            "rows": []
          },
          "tokenBindings": {
            "headerColor": "color.table.header",
            "cellPadding": "spacing.table.cell"
          }
        },
        {
          "nodeId": "n3",
          "componentId": "PrimaryButton",
          "props": { "label": "Refresh", "size": "medium" },
          "tokenBindings": { "background": "color.action.primary" }
        }
      ]
    }
    """;

    [Fact]
    public void Dashboard_sample_validates_against_intermediate_ui_schema()
    {
        var (isValid, errors) = SchemaValidator.Validate(SchemaPath, SampleTree);

        isValid.Should().BeTrue(
            "The Dashboard sample must conform to the intermediate UI schema. Errors:\n{0}", errors);
    }
}
