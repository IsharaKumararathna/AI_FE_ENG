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
    public void DataGrid_validates_against_component_catalog_schema()
    {
        var (isValid, errors) = SchemaValidator.ValidateFile(
            SchemaPath,
            Path.Combine(TestPaths.Knowledge, "components", "DataGrid.json"));

        isValid.Should().BeTrue(
            "DataGrid must conform to the component catalog schema. Errors:\n{0}", errors);
    }
}
