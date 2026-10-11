namespace VroksNet.Application.Abstractions;

/// <summary>
/// Reads and writes protobuf messages without generated code (ADR 0004): from a specification's
/// stored <c>FileDescriptorSet</c> (<see cref="Domain.ApiSpecifications.ApiSpecification.ProtoDescriptorSet"/>)
/// and a message's full name. JSON is the proto3 JSON mapping: what <see cref="ToJson"/> writes is its
/// canonical form, which is what the stored schemas describe; <see cref="ToBinary"/> also accepts the
/// other forms the mapping allows (original field names, 64-bit integers as numbers, enums by number).
/// Neither throws for bad input: a failure comes back as a readable error.
/// </summary>
public interface IProtobufTranscoder
{
    ProtobufEncoding ToBinary(byte[] descriptorSet, string messageType, string json);

    ProtobufDecoding ToJson(byte[] descriptorSet, string messageType, ReadOnlyMemory<byte> message);

    /// <summary>The request and response message types of a method, by its path "/package.Service/Method"; null when the set has no such method.</summary>
    RpcMethodTypes? FindMethod(byte[] descriptorSet, string methodPath);
}
