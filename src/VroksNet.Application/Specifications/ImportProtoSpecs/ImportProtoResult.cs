namespace VroksNet.Application.Specifications.ImportProtoSpecs;

/// <summary>The specifications an import created or updated, and the files it used only as imports.</summary>
public sealed record ImportProtoResult(IReadOnlyList<ImportedProtoSpecification> Specifications, IReadOnlyList<string> ImportOnlyFiles);
