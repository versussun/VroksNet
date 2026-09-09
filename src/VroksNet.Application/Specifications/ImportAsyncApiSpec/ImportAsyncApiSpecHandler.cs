using Mediator;
using Microsoft.Extensions.Logging;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.MockEndpoints;

namespace VroksNet.Application.Specifications.ImportAsyncApiSpec;

/// <summary>
/// Parses an AsyncAPI YAML document and stores it, replacing any existing specification with the
/// same <c>info.title</c> — see docs/project-brief.md section 2 "Версионирование спецификаций".
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

        var specification = await repository.FindByTitleAsync(parsed.Title, cancellationToken)
            ?? new ApiSpecification
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow
            };

        specification.Title = parsed.Title;
        specification.Kind = SpecificationKind.AsyncApi;
        specification.RawContent = request.YamlContent;
        specification.UpdatedAt = DateTimeOffset.UtcNow;
        specification.Endpoints = parsed.Operations
            .Select(operation => new MockEndpoint
            {
                Id = Guid.NewGuid(),
                SpecificationId = specification.Id,
                OperationKey = operation.OperationKey,
                ExampleTemplate = operation.ExampleJson,
                ResponseSchema = operation.ResponseSchemaJson
            })
            .ToList();

        await repository.UpsertAsync(specification, cancellationToken);

        logger.LogInformation(
            "Imported AsyncAPI spec '{Title}' ({FileName}) with {EndpointCount} endpoint(s): {Endpoints}",
            parsed.Title, request.FileName, parsed.Operations.Count, string.Join(", ", parsed.Operations.Select(o => o.OperationKey)));

        return specification.Id;
    }
}
