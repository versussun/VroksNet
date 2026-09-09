namespace VroksNet.Application.Abstractions;

/// <summary>Validates a JSON instance against a JSON Schema — the building block for the contract-testing checks described in docs/contract-testing-plan.md.</summary>
public interface ISchemaValidator
{
    /// <param name="schemaJson">A self-contained JSON Schema document (local `$ref`s already inlined — see <see cref="ParsedOperation"/>).</param>
    /// <param name="instanceJson">The JSON value being checked against it.</param>
    SchemaValidationResult Validate(string schemaJson, string instanceJson);
}

/// <summary><see cref="Errors"/> is empty when <see cref="IsValid"/> is true; each entry is a short, UI-safe description of one violation.</summary>
public sealed record SchemaValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static readonly SchemaValidationResult Valid = new(true, []);
}
