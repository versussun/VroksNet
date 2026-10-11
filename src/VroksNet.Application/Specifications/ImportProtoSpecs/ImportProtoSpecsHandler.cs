using Mediator;
using Microsoft.Extensions.Logging;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.MockEndpoints;

namespace VroksNet.Application.Specifications.ImportProtoSpecs;

/// <summary>
/// Parses .proto files and stores one specification per file that declares services (ADR 0004).
/// Like the OpenAPI and AsyncAPI imports, a specification with the same title is updated in place
/// (<see cref="ApiSpecification.ApplyReimport"/>), so operations still in it keep their ids. The
/// title includes the service names, so a file whose services changed makes a new specification;
/// the result notes the old one from the same package, which stays.
/// </summary>
public sealed class ImportProtoSpecsHandler(
    IProtoSpecificationParser parser,
    IApiSpecificationRepository repository,
    TimeProvider timeProvider,
    ILogger<ImportProtoSpecsHandler> logger) : IRequestHandler<ImportProtoSpecs, ImportProtoResult>
{
    public async ValueTask<ImportProtoResult> Handle(ImportProtoSpecs request, CancellationToken cancellationToken)
    {
        var parsed = parser.Parse(request.Files);
        var importedTitles = parsed.Specifications.Select(spec => spec.Title).ToHashSet(StringComparer.Ordinal);
        var stored = await repository.ListAsync(cancellationToken);

        var results = new List<ImportedProtoSpecification>();
        foreach (var spec in parsed.Specifications)
        {
            var replaced = stored.Any(existing => existing.Title == spec.Title);
            var id = await repository.UpsertAsync(ToSpecification(spec), cancellationToken);
            results.Add(new ImportedProtoSpecification(id, spec.Title, spec.Path, spec.Operations.Count, replaced, replaced ? null : NoteFor(spec.Title, stored, importedTitles)));
        }

        logger.LogInformation(
            "Imported {Count} .proto specification(s): {Titles}; import-only files: {ImportOnly}",
            results.Count, string.Join(", ", results.Select(result => result.Title)), string.Join(", ", parsed.ImportOnlyFiles));

        return new ImportProtoResult(results, parsed.ImportOnlyFiles);
    }

    private ApiSpecification ToSpecification(ParsedProtoSpecification parsed)
    {
        var now = timeProvider.GetUtcNow();
        var specification = new ApiSpecification
        {
            Id = Guid.NewGuid(),
            Title = parsed.Title,
            Kind = SpecificationKind.Proto,
            RawContent = parsed.Content,
            ProtoPath = parsed.Path,
            ProtoImports = new Dictionary<string, string>(parsed.Imports),
            ProtoDescriptorSet = parsed.DescriptorSet,
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
                RequestSchema = operation.RequestSchemaJson,
                ResponseSchema = operation.ResponseSchemaJson,
                RpcKind = operation.RpcKind
            })
            .ToList();
        return specification;
    }

    /// <summary>A new specification's note naming the stored ones from its package that it doesn't replace; null when there are none.</summary>
    private static string? NoteFor(string title, IReadOnlyList<ApiSpecification> stored, IReadOnlySet<string> importedTitles)
    {
        var prefix = ProtoTitle.PrefixOf(title);
        var others = stored
            .Where(existing => existing.Kind == SpecificationKind.Proto
                && !importedTitles.Contains(existing.Title)
                && ProtoTitle.PrefixOf(existing.Title) == prefix)
            .Select(existing => $"\"{existing.Title}\"")
            .ToList();
        return others.Count == 0
            ? null
            : $"{string.Join(", ", others)} {(others.Count == 1 ? "comes" : "come")} from the same package but {(others.Count == 1 ? "has" : "have")} other services, so {(others.Count == 1 ? "it was" : "they were")} kept as {(others.Count == 1 ? "a separate specification" : "separate specifications")}.";
    }
}
