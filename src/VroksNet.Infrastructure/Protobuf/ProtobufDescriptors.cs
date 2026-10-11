using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace VroksNet.Infrastructure.Protobuf;

/// <summary>
/// A stored <c>FileDescriptorSet</c> loaded into Google.Protobuf's runtime descriptors, with its
/// messages by full name and its methods by path. The set holds each file after the files it
/// imports (how <c>ProtoSpecificationParser</c> stores it), which is the order loading needs.
/// </summary>
internal sealed class ProtobufDescriptors
{
    private readonly Dictionary<string, MessageDescriptor> _messages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MethodDescriptor> _methods = new(StringComparer.Ordinal);

    private ProtobufDescriptors(IReadOnlyList<FileDescriptor> files)
    {
        foreach (var file in files)
        {
            foreach (var message in file.MessageTypes)
            {
                Add(message);
            }

            foreach (var method in file.Services.SelectMany(service => service.Methods))
            {
                _methods[$"/{method.Service.FullName}/{method.Name}"] = method;
            }
        }
    }

    public static ProtobufDescriptors Load(byte[] descriptorSet)
    {
        var set = FileDescriptorSet.Parser.ParseFrom(descriptorSet);
        return new ProtobufDescriptors(FileDescriptor.BuildFromByteStrings(set.File.Select(file => file.ToByteString())));
    }

    public MessageDescriptor? FindMessage(string fullName) => _messages.GetValueOrDefault(fullName.TrimStart('.'));

    public MethodDescriptor? FindMethod(string path) => _methods.GetValueOrDefault(path);

    private void Add(MessageDescriptor message)
    {
        _messages[message.FullName] = message;
        foreach (var nested in message.NestedTypes)
        {
            Add(nested);
        }
    }
}
