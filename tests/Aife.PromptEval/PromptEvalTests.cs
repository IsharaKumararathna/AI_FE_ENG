using FluentAssertions;
using Xunit;

namespace Aife.PromptEval;

public class PromptEvalTests
{
    [Fact]
    public void Evaluate_returns_passing_result_when_accuracy_above_threshold()
    {
        var dataset = new List<ScoredDatasetEntry>
        {
            new() { Input = "button", ExpectedOutput = "BUSButton", Category = "mapper" },
            new() { Input = "table", ExpectedOutput = "BUSGrid", Category = "mapper" },
            new() { Input = "unknown", ExpectedOutput = "null", Category = "mapper" }
        };

        var harness = new PromptEvaluationHarness(input => input switch
        {
            "button" => ("BUSButton", 100, 50),
            "table" => ("BUSGrid", 120, 60),
            _ => ("null", 80, 40)
        });

        var result = harness.Evaluate("component.mapper", "1.0.0", dataset, threshold: 0.8);

        result.Accuracy.Should().Be(1.0);
        result.Passed.Should().BeTrue();
        result.TotalEntries.Should().Be(3);
        result.CorrectCount.Should().Be(3);
        result.AverageTokenCost.Should().Be(100.0); // (100+120+80)/3
    }

    [Fact]
    public void Evaluate_returns_failing_result_when_accuracy_below_threshold()
    {
        var dataset = new List<ScoredDatasetEntry>
        {
            new() { Input = "button", ExpectedOutput = "BUSButton", Category = "mapper" },
            new() { Input = "table", ExpectedOutput = "WrongComponent", Category = "mapper" }
        };

        var harness = new PromptEvaluationHarness(input => input switch
        {
            "button" => ("BUSButton", 100, 50),
            _ => ("BUSGrid", 120, 60)
        });

        var result = harness.Evaluate("component.mapper", "1.0.0", dataset, threshold: 0.8);

        result.Accuracy.Should().Be(0.5);
        result.Passed.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_records_latency_and_token_metrics()
    {
        var dataset = new List<ScoredDatasetEntry>
        {
            new() { Input = "a", ExpectedOutput = "X", Category = "test" },
            new() { Input = "b", ExpectedOutput = "Y", Category = "test" }
        };

        var harness = new PromptEvaluationHarness(input => input switch
        {
            "a" => ("X", 50, 100),
            _ => ("Y", 150, 300)
        });

        var result = harness.Evaluate("test.prompt", "2.0.0", dataset);

        result.AverageTokenCost.Should().Be(100.0); // (50+150)/2
        result.AverageLatencyMs.Should().Be(200.0); // (100+300)/2
    }

    [Fact]
    public void ToString_includes_key_metrics()
    {
        var dataset = new List<ScoredDatasetEntry>
        {
            new() { Input = "x", ExpectedOutput = "Y", Category = "test" }
        };

        var harness = new PromptEvaluationHarness(_ => ("Y", 100, 50));
        var result = harness.Evaluate("test.prompt", "1.0.0", dataset);

        var summary = result.ToString();
        summary.Should().Contain("test.prompt");
        summary.Should().Contain("1/1");
        summary.Should().Contain("PASSED");
    }

    [Fact]
    public void StructuralReactScorer_penalizes_hardcoded_hex()
    {
        var scorer = new StructuralReactScorer();
        var clean = scorer.Score("import { Button } from '../DesignSystem'; export const X = () => <Button variant='primary'>Save</Button>;");
        var dirty = scorer.Score("import { Button } from '../DesignSystem'; export const X = () => <button style={{background:'#1548be'}}>Save</button>;");

        clean.Should().BeGreaterThan(0.9);
        dirty.Should().BeLessThan(clean);
        dirty.Should().BeLessThan(0.8, "hardcoded hex + inline style should fail the entry threshold");
    }

    [Fact]
    public void StructuralReactScorer_rejects_banned_packages()
    {
        var scorer = new StructuralReactScorer();
        var score = scorer.Score("import { Button } from '@progress/kendo-react-buttons'; export const X = () => <Button/>;");

        score.Should().BeLessThan(0.6, "Kendo import is a banned package and must tank the score");
    }

    [Fact]
    public void EvaluateStructural_counts_entry_passing_when_score_above_threshold()
    {
        var dataset = new List<ScoredDatasetEntry>
        {
            new() { Input = "good", ExpectedOutput = "", Category = "react" },
            new() { Input = "bad", ExpectedOutput = "", Category = "react" }
        };

        var harness = new PromptEvaluationHarness(input => input switch
        {
            "good" => ("import { Button } from '../DesignSystem'; export const P = () => <Button variant='primary'>Save</Button>;", 100, 50),
            _ => ("<button style={{background:'#fff'}}>Save</button>", 80, 40)
        });

        var result = harness.EvaluateStructural("react.generator", "1.1.0", dataset, entryPassScore: 0.8);

        result.CorrectCount.Should().Be(1, "only the clean output should pass");
        result.TotalEntries.Should().Be(2);
    }
}
