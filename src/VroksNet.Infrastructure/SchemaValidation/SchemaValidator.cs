using System.Text.Json.Nodes;
using Json.Schema;
using VroksNet.Application.Abstractions;

namespace VroksNet.Infrastructure.SchemaValidation;

/// <summary>Validates JSON instances against JSON Schema via <c>JsonSchema.Net</c> — see docs/contract-testing-plan.md.</summary>
public sealed class SchemaValidator : ISchemaValidator
{
    private static readonly EvaluationOptions Options = new() { OutputFormat = OutputFormat.List };

    public SchemaValidationResult Validate(string schemaJson, string instanceJson)
    {
        JsonSchema schema;
        JsonNode? instance;

        try
        {
            schema = JsonSchema.FromText(schemaJson);
            instance = JsonNode.Parse(instanceJson);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SchemaValidationResult(false, [$"Not valid JSON: {ex.Message}"]);
        }

        var results = schema.Evaluate(instance, Options);
        if (results.IsValid)
        {
            return SchemaValidationResult.Valid;
        }

        // "List" output format flattens results into Details — one entry per evaluated
        // subschema/keyword, each with its own Errors. Collect every failing one's messages
        // rather than just the top-level result, which is often just "one or more failed".
        var errors = results.Details
            .Where(detail => !detail.IsValid && detail.HasErrors)
            .SelectMany(detail => detail.Errors!.Values)
            .Distinct()
            .ToList();

        if (errors.Count == 0 && results.HasErrors)
        {
            errors = results.Errors!.Values.Distinct().ToList();
        }

        return new SchemaValidationResult(false, errors.Count > 0 ? errors : ["The instance doesn't match the schema."]);
    }
}
