using Aife.Application.Knowledge;
using FluentAssertions;
using Newtonsoft.Json;
using Xunit;

namespace Aife.Knowledge.Tests;

/// <summary>
/// Verifies that <see cref="KnowledgeTrainerService"/> can extract components
/// written in the plain JS/JSX + PropTypes convention used by the BUS core
/// repos (BUSKvalitet), not just TypeScript interfaces.
/// </summary>
public sealed class KnowledgeTrainerServicePropTypesTests : IDisposable
{
    private readonly string _sourceRoot;
    private readonly string _knowledgeRoot;

    public KnowledgeTrainerServicePropTypesTests()
    {
        _sourceRoot = Path.Combine(Path.GetTempPath(), $"aife-trainer-src-{Guid.NewGuid():N}");
        _knowledgeRoot = Path.Combine(Path.GetTempPath(), $"aife-trainer-kb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sourceRoot);
        Directory.CreateDirectory(_knowledgeRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_sourceRoot, true); } catch { /* best-effort cleanup */ }
        try { Directory.Delete(_knowledgeRoot, true); } catch { /* best-effort cleanup */ }
    }

    private void WriteComponentFile(string relativeFolder, string fileName, string content)
    {
        var dir = Path.Combine(_sourceRoot, "src", "Components", "CustomUIs", relativeFolder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), content);
    }

    private ComponentDetail LoadExtractedComponent(string componentId)
    {
        var path = Path.Combine(_knowledgeRoot, "components", $"{componentId}.json");
        File.Exists(path).Should().BeTrue($"expected {componentId}.json to be written by the trainer");
        var json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<ComponentDetail>(json)!;
    }

    [Fact]
    public async Task ExtractsPropsFromNamedExportWithForwardRefAndSimplePropTypes()
    {
        // Mirrors BUSButton.js: export const X = forwardRef(...); X.propTypes = {...};
        WriteComponentFile("BUSButtons", "BUSButton.js", """
            import { forwardRef } from 'react';
            import PropTypes from 'prop-types';
            import './BUSButton.scss';

            export const BUSButton = forwardRef(({ onClick, disabled, className, children }, ref) => (
                <button ref={ref} onClick={onClick} disabled={disabled} className={className}>
                    {children}
                </button>
            ));

            BUSButton.propTypes = {
                onClick: PropTypes.func,
                disabled: PropTypes.bool,
                className: PropTypes.string,
                children: PropTypes.node
            };
            """);

        var trainer = new KnowledgeTrainerService(_knowledgeRoot);
        var result = await trainer.TrainFromFolderAsync(_sourceRoot, TrainMode.Replace, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ComponentsExtracted.Should().Be(1);

        var component = LoadExtractedComponent("BUSButton");
        component.ExportName.Should().Be("BUSButton");
        component.IsDefaultExport.Should().BeFalse();
        component.ImportPath.Should().Be("Components/CustomUIs/BUSButtons/BUSButton");
        component.Props.Should().Contain(p => p.Name == "onClick" && p.Type == "function");
        component.Props.Should().Contain(p => p.Name == "disabled" && p.Type == "boolean");
        component.Props.Should().Contain(p => p.Name == "className" && p.Type == "string");
        component.Props.Should().Contain(p => p.Name == "children" && p.Type == "ReactNode");
    }

    [Fact]
    public async Task DetectsRequiredPropsIncludingNestedShapes()
    {
        // Mirrors BUSGrid-style nested PropTypes (arrayOf(shape({...}))) plus a
        // required simple prop, to confirm the bracket-depth-aware split works.
        WriteComponentFile("bus-grids/bus-grid", "bus-grid.jsx", """
            import PropTypes from 'prop-types';

            export function BUSGrid({ gridKeyField, data, tabs }) {
                return <div className="bus-grid" />;
            }

            BUSGrid.propTypes = {
                gridKeyField: PropTypes.string.isRequired,
                data: PropTypes.array,
                tabs: PropTypes.arrayOf(PropTypes.shape({
                    title: PropTypes.string,
                    count: PropTypes.string
                })).isRequired
            };
            """);

        var trainer = new KnowledgeTrainerService(_knowledgeRoot);
        await trainer.TrainFromFolderAsync(_sourceRoot, TrainMode.Replace, CancellationToken.None);

        var component = LoadExtractedComponent("BUSGrid");
        component.Props.Should().Contain(p => p.Name == "gridKeyField" && p.Required);
        component.Props.Should().Contain(p => p.Name == "data" && !p.Required);
        component.Props.Should().Contain(p => p.Name == "tabs" && p.Required && p.Type == "array");
    }

    [Fact]
    public async Task ExtractsDefaultExportedComponentByReassignment()
    {
        // Mirrors BUSDatePickerWrapper.jsx: const X = (...) => {...}; export default X;
        WriteComponentFile("BUSDatePickerWrapper", "BUSDatePickerWrapper.jsx", """
            import PropTypes from 'prop-types';

            const BUSDatePickerWrapper = ({ value, onChange, disabled }) => {
                return <input type="date" value={value} disabled={disabled} />;
            };

            BUSDatePickerWrapper.propTypes = {
                value: PropTypes.string,
                onChange: PropTypes.func.isRequired,
                disabled: PropTypes.bool
            };

            export default BUSDatePickerWrapper;
            """);

        var trainer = new KnowledgeTrainerService(_knowledgeRoot);
        var result = await trainer.TrainFromFolderAsync(_sourceRoot, TrainMode.Replace, CancellationToken.None);

        result.ComponentsExtracted.Should().Be(1);

        var component = LoadExtractedComponent("BUSDatePickerWrapper");
        component.IsDefaultExport.Should().BeTrue();
        component.ExportName.Should().Be("BUSDatePickerWrapper");
        component.Props.Should().Contain(p => p.Name == "onChange" && p.Required && p.Type == "function");
    }

    [Fact]
    public async Task ExtractsCssCustomPropertyTokensAlongsideScssVariables()
    {
        var stylesDir = Path.Combine(_sourceRoot, "src", "Styles", "Basic");
        Directory.CreateDirectory(stylesDir);
        File.WriteAllText(Path.Combine(stylesDir, "Variables.scss"), """
            $color-primary: #1548be;

            :root {
                --color-secondary: #ffffff;
                --spacing-md: 16px;
            }
            """);

        var trainer = new KnowledgeTrainerService(_knowledgeRoot);
        var result = await trainer.TrainFromFolderAsync(_sourceRoot, TrainMode.Replace, CancellationToken.None);

        result.TokensExtracted.Should().Be(3);

        var tokensJson = File.ReadAllText(Path.Combine(_knowledgeRoot, "tokens", "tokens.json"));
        tokensJson.Should().Contain("color-primary");
        tokensJson.Should().Contain("color-secondary");
        tokensJson.Should().Contain("spacing-md");
    }
}
