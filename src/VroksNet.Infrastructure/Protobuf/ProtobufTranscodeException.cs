namespace VroksNet.Infrastructure.Protobuf;

/// <summary>Input that isn't the message it should be; the message is safe to show and names where ("lines[0].quantity: …").</summary>
internal sealed class ProtobufTranscodeException(string message) : Exception(message)
{
    public static ProtobufTranscodeException At(string path, string message)
        => new(path.Length == 0 ? message : $"{path}: {message}");
}
