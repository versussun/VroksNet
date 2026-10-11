namespace VroksNet.Application.Abstractions;

/// <summary>What <see cref="IProtoSpecificationParser"/> found: one specification per file that declares a service, and the paths of the files that are only imports.</summary>
public sealed record ParsedProtoUpload(IReadOnlyList<ParsedProtoSpecification> Specifications, IReadOnlyList<string> ImportOnlyFiles);
