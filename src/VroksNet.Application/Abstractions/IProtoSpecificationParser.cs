namespace VroksNet.Application.Abstractions;

/// <summary>
/// Parses .proto files (ADR 0004). Unlike <see cref="ISpecificationParser"/> it takes several files
/// at once, since a .proto file can import others: every file that declares a service becomes a
/// specification of its own, and the rest are only imports.
/// </summary>
public interface IProtoSpecificationParser
{
    /// <param name="files">The files' text keyed by path; imports resolve among these paths. The well-known types (<c>google/protobuf/*.proto</c>) needn't be given.</param>
    /// <exception cref="SpecificationParseException">A file doesn't parse, an import is missing, a construct isn't supported, or no file declares a service.</exception>
    ParsedProtoUpload Parse(IReadOnlyDictionary<string, string> files);
}
