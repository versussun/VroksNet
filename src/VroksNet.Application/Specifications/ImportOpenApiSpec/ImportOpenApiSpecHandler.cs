using Mediator;
using Microsoft.Extensions.Logging;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.MockEndpoints;

namespace VroksNet.Application.Specifications.ImportOpenApiSpec;

/// <summary>
/// Parses an OpenAPI YAML document and stores it, replacing any existing specification with the
/// same <c>info.title</c> — see docs/project-brief.md section 2 "Версионирование спецификаций".
/// </summary>
public sealed class ImportOpenApiSpecHandler(
    ISpecificationParser parser,
    IApiSpecificationRepository repository,
    ILogger<ImportOpenApiSpecHandler> logger) : IRequestHandler<ImportOpenApiSpec, Guid>
{
    public async ValueTask<Guid> Handle(ImportOpenApiSpec request, CancellationToken cancellationToken)
    {
        var parsed = await parser.ParseAsync(request.YamlContent, cancellationToken);

        var existing = await repository.FindByTitleAsync(parsed.Title, cancellationToken);

        // Re-importing replaces the endpoints; keep provider mode on for operations that are still
        // there (same key, so no new overlap with other specs can have appeared).
        var servedKeys = existing?.Endpoints.Where(e => e.ServeAtRealPath).Select(e => e.OperationKey).ToHashSet(StringComparer.Ordinal) ?? [];

        var specification = existing
            ?? new ApiSpecification
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow
            };

        specification.Title = parsed.Title;
        specification.Kind = SpecificationKind.OpenApi;
        specification.RawContent = request.YamlContent;
        specification.UpdatedAt = DateTimeOffset.UtcNow;
        specification.Endpoints = parsed.Operations
            .Select(operation => new MockEndpoint
            {
                Id = Guid.NewGuid(),
                SpecificationId = specification.Id,
                OperationKey = operation.OperationKey,
                ExampleTemplate = operation.ExampleJson,
                RequestSchema = operation.RequestSchemaJson,
                ResponseSchema = operation.ResponseSchemaJson,
                ResponseSchemasByStatus = operation.ResponseSchemasByStatus?.ToDictionary() ?? [],
                ServeAtRealPath = servedKeys.Contains(operation.OperationKey)
            })
            .ToList();

        await repository.UpsertAsync(specification, cancellationToken);

        logger.LogInformation(
            "Imported OpenAPI spec '{Title}' ({FileName}) with {EndpointCount} endpoint(s): {Endpoints}",
            parsed.Title, request.FileName, parsed.Operations.Count, string.Join(", ", parsed.Operations.Select(o => o.OperationKey)));

        return specification.Id;
    }
}
