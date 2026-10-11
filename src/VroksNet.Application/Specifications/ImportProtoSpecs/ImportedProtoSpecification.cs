namespace VroksNet.Application.Specifications.ImportProtoSpecs;

/// <summary>
/// One specification an import stored. <see cref="Replaced"/> is true when one with the same title
/// existed and was updated in place. <see cref="Note"/> says when another specification from the
/// same package was left as it is, because its services differ (see <see cref="Domain.ApiSpecifications.ProtoTitle"/>).
/// </summary>
public sealed record ImportedProtoSpecification(Guid Id, string Title, string Path, int OperationCount, bool Replaced, string? Note);
