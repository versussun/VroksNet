using Mediator;

namespace VroksNet.Application.Specifications.ImportProtoSpecs;

/// <summary>Imports .proto files (ADR 0004): their text keyed by path, imports resolving among those paths.</summary>
public sealed record ImportProtoSpecs(IReadOnlyDictionary<string, string> Files) : IRequest<ImportProtoResult>;
