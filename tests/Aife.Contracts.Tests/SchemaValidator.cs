using System.Text.Json.Nodes;
using Json.Schema;

namespace Aife.Contracts.Tests;

/// <summary>
/// Loads a JSON Schema from disk and validates a JSON instance against it.
/// Uses JsonSchema.Net (draft 2020-12) with System.Text.Json.Nodes only within
/// test infrastructure; application code uses Newtonsoft.Json per convention.
/// </summary>
internal static class SchemaValidator
{
    public static (bool IsValid, string Errors) Validate(string schemaPath, string instanceJson)
    {
        var schemaText = File.ReadAllText(schemaPath);
        var schema = JsonSchema.FromText(schemaText);
        var instance = JsonNode.Parse(instanceJson);

        var results = schema.Evaluate(instance, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List
        });

        if (results.IsValid)
        {
            return (true, string.Empty);
        }

        var errorMessages = results.Details
            .Where(d => d.HasErrors)
            .SelectMany(d => d.Errors!.Select(e => $"[{d.InstanceLocation}] {e.Value}"))
            .ToList();

        return (false, string.Join("\n", errorMessages));
    }

    public static (bool IsValid, string Errors) ValidateFile(string schemaPath, string instancePath)
    {
        var instanceJson = File.ReadAllText(instancePath);
        return Validate(schemaPath, instanceJson);
    }
}
