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

        var specification = await repository.FindByTitleAsync(parsed.Title, cancellationToken)
            ?? new ApiSpecification
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow
            };

        specification.Title = parsed.Title;
        specification.Kind = SpecificationKind.OpenApi;
        specification.RawContent = request.YamlContent;
        specification.UpdatedAt = DateTimeOffset.UtcNow;
        specification.Endpoints = parsed.OperationKeys
            .Select(key => new MockEndpoint { Id = Guid.NewGuid(), SpecificationId = specification.Id, OperationKey = key })
            .ToList();

        await repository.UpsertAsync(specification, cancellationToken);

        logger.LogInformation(
            "Imported OpenAPI spec '{Title}' ({FileName}) with {EndpointCount} endpoint(s): {Endpoints}",
            parsed.Title, request.FileName, parsed.OperationKeys.Count, string.Join(", ", parsed.OperationKeys));

        return specification.Id;
    }
}
