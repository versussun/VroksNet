namespace VroksNet.Application.Abstractions;

/// <summary>
/// One .proto file that declares services, ready to store as a specification (see
/// <see cref="Domain.ApiSpecifications.ApiSpecification"/>'s <c>Proto*</c> properties).
/// </summary>
/// <param name="Title">Its <see cref="Domain.ApiSpecifications.ProtoTitle"/>.</param>
/// <param name="Path">The file's path among the parsed files.</param>
/// <param name="Content">The file's text.</param>
/// <param name="Imports">The text of every file it imports, directly or not, by path — the well-known types left out unless they were given.</param>
/// <param name="DescriptorSet">The file and everything it imports as a serialized <c>google.protobuf.FileDescriptorSet</c>, each file after its imports.</param>
/// <param name="Operations">One per RPC method, keyed "RPC /package.Service/Method", with <see cref="ParsedOperation.RpcKind"/> set.</param>
public sealed record ParsedProtoSpecification(
    string Title,
    string Path,
    string Content,
    IReadOnlyDictionary<string, string> Imports,
    byte[] DescriptorSet,
    IReadOnlyList<ParsedOperation> Operations);
