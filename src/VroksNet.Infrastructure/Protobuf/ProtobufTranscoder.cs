using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using VroksNet.Application.Abstractions;

namespace VroksNet.Infrastructure.Protobuf;

/// <summary>
/// <see cref="IProtobufTranscoder"/> over <see cref="ProtobufJsonEncoder"/> and <see cref="ProtobufJsonDecoder"/>
/// (ADR 0004). A singleton: each distinct descriptor set is loaded once and kept by its hash, so a
/// re-imported specification with a changed set simply gets a new entry. The cache is small and
/// dropped whole when it fills — specifications are few, and loading one is cheap.
/// </summary>
public sealed class ProtobufTranscoder : IProtobufTranscoder
{
    private const int MaxCachedSets = 64;
    private static readonly JsonWriterOptions Indented = new() { Indented = true };

    private readonly ConcurrentDictionary<string, ProtobufDescriptors> _cache = new(StringComparer.Ordinal);

    public ProtobufEncoding ToBinary(byte[] descriptorSet, string messageType, string json)
    {
        try
        {
            var descriptors = DescriptorsOf(descriptorSet);
            var message = descriptors.FindMessage(messageType)
                ?? throw new ProtobufTranscodeException($"The .proto files declare no message {messageType}.");
            using var document = JsonDocument.Parse(json);
            return new ProtobufEncoding(new ProtobufJsonEncoder(descriptors).Encode(message, document.RootElement), null);
        }
        catch (JsonException ex)
        {
            return ProtobufEncoding.Failed($"Not valid JSON: {ex.Message}");
        }
        catch (ProtobufTranscodeException ex)
        {
            return ProtobufEncoding.Failed(ex.Message);
        }
    }

    public ProtobufDecoding ToJson(byte[] descriptorSet, string messageType, ReadOnlyMemory<byte> message)
    {
        try
        {
            var descriptors = DescriptorsOf(descriptorSet);
            var type = descriptors.FindMessage(messageType)
                ?? throw new ProtobufTranscodeException($"The .proto files declare no message {messageType}.");
            var decoder = new ProtobufJsonDecoder(descriptors);
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, Indented))
            {
                decoder.Decode(writer, type, message);
            }

            return new ProtobufDecoding(System.Text.Encoding.UTF8.GetString(buffer.ToArray()), null, decoder.Warnings);
        }
        catch (ProtobufTranscodeException ex)
        {
            return ProtobufDecoding.Failed(ex.Message);
        }
    }

    public RpcMethodTypes? FindMethod(byte[] descriptorSet, string methodPath)
    {
        try
        {
            return DescriptorsOf(descriptorSet).FindMethod(methodPath) is { } method
                ? new RpcMethodTypes(method.InputType.FullName, method.OutputType.FullName)
                : null;
        }
        catch (ProtobufTranscodeException)
        {
            return null; // descriptors that don't load hold no method
        }
    }

    private ProtobufDescriptors DescriptorsOf(byte[] descriptorSet)
    {
        var key = Convert.ToHexString(SHA256.HashData(descriptorSet));
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        ProtobufDescriptors loaded;
        try
        {
            loaded = ProtobufDescriptors.Load(descriptorSet);
        }
        catch (Exception ex) when (ex is InvalidProtocolBufferException or Google.Protobuf.Reflection.DescriptorValidationException or ArgumentException)
        {
            throw new ProtobufTranscodeException($"The stored descriptors can't be loaded: {ex.Message}");
        }

        if (_cache.Count >= MaxCachedSets)
        {
            _cache.Clear();
        }

        return _cache.GetOrAdd(key, loaded);
    }
}
