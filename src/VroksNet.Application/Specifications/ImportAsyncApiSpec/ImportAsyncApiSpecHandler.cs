using Mediator;
using Microsoft.Extensions.Logging;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.MockEndpoints;

namespace VroksNet.Application.Specifications.ImportAsyncApiSpec;

/// <summary>
/// Parses an AsyncAPI YAML document and stores it. An existing specification with the same
/// <c>info.title</c> is updated in place, idempotently — see docs/project-brief.md section 2
/// "Specification versioning" and <see cref="ApiSpecification.ApplyReimport"/>: operations that
/// are still in the spec keep their ids, so Publishers and Test Scenarios on them keep working.
/// Mirrors <c>ImportOpenApiSpecHandler</c>; kept as its own vertical (own request, own handler,
/// own parser interface) rather than branching on kind in one shared handler, matching how the
/// two kinds are already split at the endpoint level (<c>/api/specifications/openapi</c> vs.
/// <c>/api/specifications/asyncapi</c>).
/// </summary>
public sealed class ImportAsyncApiSpecHandler(
    IAsyncApiSpecificationParser parser,
    IApiSpecificationRepository repository,
    ILogger<ImportAsyncApiSpecHandler> logger) : IRequestHandler<ImportAsyncApiSpec, Guid>
{
    public async ValueTask<Guid> Handle(ImportAsyncApiSpec request, CancellationToken cancellationToken)
    {
        var parsed = await parser.ParseAsync(request.YamlContent, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var specification = new ApiSpecification
        {
            Id = Guid.NewGuid(),
            Title = parsed.Title,
            Kind = SpecificationKind.AsyncApi,
            RawContent = request.YamlContent,
            Protocols = [.. parsed.Protocols ?? []],
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
                ExampleIsGenerated = operation.ExampleIsGenerated,
                ResponseSchema = operation.ResponseSchemaJson
            })
            .ToList();

        var storedId = await repository.UpsertAsync(specification, cancellationToken);

        logger.LogInformation(
            "Imported AsyncAPI spec '{Title}' ({FileName}) with {EndpointCount} endpoint(s): {Endpoints}",
            parsed.Title, request.FileName, parsed.Operations.Count, string.Join(", ", parsed.Operations.Select(o => o.OperationKey)));

        return storedId;
    }
}
