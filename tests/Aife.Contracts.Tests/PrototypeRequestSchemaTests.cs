using FluentAssertions;
using Xunit;

namespace Aife.Contracts.Tests;

public class PrototypeRequestSchemaTests
{
    private static readonly string SchemaPath =
        Path.Combine(TestPaths.Schemas, "prototype-request.schema.json");

    private const string SampleRequest = """
    {
      "intent": "A customer dashboard with a top header, left navigation, a customer table, and an Add Customer button.",
      "pages": [
        {
          "name": "Dashboard",
          "layout": "AppLayout",
          "referencePattern": "ActiveInspectionsPage",
          "regions": [
            { "slot": "header", "component": "AppBar" },
            { "slot": "sidebar", "component": "NavList" },
            { "slot": "main", "component": "DataGrid", "props": { "title": "Customers" } }
          ],
          "actions": [
            { "slot": "main", "component": "PrimaryButton", "label": "Add Customer" }
          ]
        }
      ]
    }
    """;

    [Fact]
    public void Sample_prototype_request_validates_against_schema()
    {
        var (isValid, errors) = SchemaValidator.Validate(SchemaPath, SampleRequest);

        isValid.Should().BeTrue(
            "The sample PrototypeRequest must conform to the prototype request schema. Errors:\n{0}", errors);
    }
}
