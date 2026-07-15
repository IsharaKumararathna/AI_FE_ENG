using FluentAssertions;
using Xunit;

namespace Aife.Contracts.Tests;

public class ComponentCatalogSchemaTests
{
    private static readonly string SchemaPath =
        Path.Combine(TestPaths.Schemas, "component-catalog.schema.json");

    [Fact]
    public void BUSButton_validates_against_component_catalog_schema()
    {
        var (isValid, errors) = SchemaValidator.ValidateFile(
            SchemaPath,
            Path.Combine(TestPaths.Knowledge, "components", "BUSButton.json"));

        isValid.Should().BeTrue(
            "BUSButton must conform to the component catalog schema. Errors:\n{0}", errors);
    }

    [Fact]
    public void BUSGrid_validates_against_component_catalog_schema()
    {
        var (isValid, errors) = SchemaValidator.ValidateFile(
            SchemaPath,
            Path.Combine(TestPaths.Knowledge, "components", "BUSGrid.json"));

        isValid.Should().BeTrue(
            "BUSGrid must conform to the component catalog schema. Errors:\n{0}", errors);
    }
}
