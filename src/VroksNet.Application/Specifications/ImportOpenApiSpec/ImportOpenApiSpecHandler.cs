using Mediator;
using Microsoft.Extensions.Logging;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.MockEndpoints;

namespace VroksNet.Application.Specifications.ImportOpenApiSpec;

/// <summary>
/// Parses an OpenAPI YAML document and stores it. An existing specification with the same
/// <c>info.title</c> is updated in place, idempotently — see docs/project-brief.md section 2
/// "Specification versioning" and <see cref="ApiSpecification.ApplyReimport"/>: operations that
/// are still in the spec keep their ids, enabled state and provider mode.
/// </summary>
public sealed class ImportOpenApiSpecHandler(
    ISpecificationParser parser,
    IApiSpecificationRepository repository,
    ILogger<ImportOpenApiSpecHandler> logger) : IRequestHandler<ImportOpenApiSpec, Guid>
{
    public async ValueTask<Guid> Handle(ImportOpenApiSpec request, CancellationToken cancellationToken)
    {
        var parsed = await parser.ParseAsync(request.YamlContent, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var specification = new ApiSpecification
        {
            Id = Guid.NewGuid(),
            Title = parsed.Title,
            Kind = SpecificationKind.OpenApi,
            RawContent = request.YamlContent,
            CreatedAt = now,
            UpdatedAt = now
        };
        specification.Endpoints = parsed.Operations
            .Select((operation, position) => new MockEndpoint
            {
                Id = Guid.NewGuid(),
                SpecificationId = specification.Id,
                OperationKey = operation.OperationKey,
                Position = position,
                ExampleTemplate = operation.ExampleJson,
                ExampleStatusCode = operation.ExampleStatusCode,
                RequestSchema = operation.RequestSchemaJson,
                ResponseSchema = operation.ResponseSchemaJson,
                ResponseSchemasByStatus = operation.ResponseSchemasByStatus?.ToDictionary() ?? []
            })
            .ToList();

        var storedId = await repository.UpsertAsync(specification, cancellationToken);

        logger.LogInformation(
            "Imported OpenAPI spec '{Title}' ({FileName}) with {EndpointCount} endpoint(s): {Endpoints}",
            parsed.Title, request.FileName, parsed.Operations.Count, string.Join(", ", parsed.Operations.Select(o => o.OperationKey)));

        return storedId;
    }
}
