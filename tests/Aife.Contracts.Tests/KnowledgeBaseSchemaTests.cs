using FluentAssertions;
using Xunit;

namespace Aife.Contracts.Tests;

public class KnowledgeBaseSchemaTests
{
    [Fact]
    public void Manifest_validates_against_knowledge_manifest_schema()
    {
        var (isValid, errors) = SchemaValidator.ValidateFile(
            Path.Combine(TestPaths.Schemas, "knowledge-manifest.schema.json"),
            Path.Combine(TestPaths.Knowledge, "manifest.json"));

        isValid.Should().BeTrue(
            "manifest.json must conform to the knowledge manifest schema. Errors:\n{0}", errors);
    }

    [Fact]
    public void Tokens_validates_against_design_token_set_schema()
    {
        var (isValid, errors) = SchemaValidator.ValidateFile(
            Path.Combine(TestPaths.Schemas, "design-token-set.schema.json"),
            Path.Combine(TestPaths.Knowledge, "tokens", "tokens.json"));

        isValid.Should().BeTrue(
            "tokens.json must conform to the design token set schema. Errors:\n{0}", errors);
    }

    [Fact]
    public void AppLayout_validates_against_layout_pattern_schema()
    {
        var (isValid, errors) = SchemaValidator.ValidateFile(
            Path.Combine(TestPaths.Schemas, "layout-pattern.schema.json"),
            Path.Combine(TestPaths.Knowledge, "layouts", "AppLayout.json"));

        isValid.Should().BeTrue(
            "AppLayout.json must conform to the layout pattern schema. Errors:\n{0}", errors);
    }

    [Fact]
    public void ActiveInspectionsPage_validates_against_reference_ui_pattern_schema()
    {
        var (isValid, errors) = SchemaValidator.ValidateFile(
            Path.Combine(TestPaths.Schemas, "reference-ui-pattern.schema.json"),
            Path.Combine(TestPaths.Knowledge, "referenceUiPatterns", "ActiveInspectionsPage.json"));

        isValid.Should().BeTrue(
            "ActiveInspectionsPage.json must conform to the reference UI pattern schema. Errors:\n{0}", errors);
    }
}
