namespace VroksNet.Web;

/// <summary>One specification a .proto import created, or updated in place (<see cref="Replaced"/>); <see cref="Note"/> names a specification from the same package it left as it is.</summary>
public sealed record ImportedProtoSpecification(Guid Id, string Title, string Path, int OperationCount, bool Replaced, string? Note);
