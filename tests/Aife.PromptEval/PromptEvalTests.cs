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
            new() { Input = "table", ExpectedOutput = "DataGrid", Category = "mapper" },
            new() { Input = "unknown", ExpectedOutput = "null", Category = "mapper" }
        };

        var harness = new PromptEvaluationHarness(input => input switch
        {
            "button" => ("BUSButton", 100, 50),
            "table" => ("DataGrid", 120, 60),
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
            _ => ("DataGrid", 120, 60)
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
}
