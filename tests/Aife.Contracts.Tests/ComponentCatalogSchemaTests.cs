using FluentAssertions;
using Xunit;

namespace Aife.Contracts.Tests;

public class ComponentCatalogSchemaTests
{
    private static readonly string SchemaPath =
        Path.Combine(TestPaths.Schemas, "component-catalog.schema.json");

    [Fact]
    public void PrimaryButton_validates_against_component_catalog_schema()
    {
        var (isValid, errors) = SchemaValidator.ValidateFile(
            SchemaPath,
            Path.Combine(TestPaths.Knowledge, "components", "PrimaryButton.json"));

        isValid.Should().BeTrue(
            "PrimaryButton must conform to the component catalog schema. Errors:\n{0}", errors);
    }

    [Fact]
    public void DataTable_validates_against_component_catalog_schema()
    {
        var (isValid, errors) = SchemaValidator.ValidateFile(
            SchemaPath,
            Path.Combine(TestPaths.Knowledge, "components", "DataTable.json"));

        isValid.Should().BeTrue(
            "DataTable must conform to the component catalog schema. Errors:\n{0}", errors);
    }
}
