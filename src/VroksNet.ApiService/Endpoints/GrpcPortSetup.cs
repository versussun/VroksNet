using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using Mediator;
using VroksNet.Application.Mocking.InvokeRpcMock;

namespace VroksNet.ApiService.Endpoints;

/// <summary>
/// The gRPC mock's port (ADR 0004): HTTP/2 without TLS (h2c, bound by <see cref="ListenSetup"/>),
/// where every call is answered by <see cref="InvokeRpcMock"/> at the method's real path. This is the
/// wire protocol only — gRPC's length-prefixed frames (identity or gzip), its <c>grpc-timeout</c>
/// deadline and its status trailers; which operation answers, and with what, is the use case's.
/// A failure without a response message is sent "trailers-only": the status in the headers.
/// </summary>
public static class GrpcPortSetup
{
    /// <summary>gRPC's own default limit for one message.</summary>
    public const int MaxMessageBytes = 4 * 1024 * 1024;

    private const int MaxRequestBytes = 4 * MaxMessageBytes;
    private const int ResourceExhausted = 8;
    private const string SupportedEncodings = "identity,gzip";

    /// <summary>Headers that are the protocol's own, not the caller's metadata.</summary>
    private static readonly HashSet<string> ProtocolHeaders = new(StringComparer.OrdinalIgnoreCase) { "content-type", "te", "host", "user-agent", "content-length" };

    /// <summary>Branches every request on <paramref name="port"/> into the gRPC mock, ahead of everything else.</summary>
    public static void MapGrpcPort(this WebApplication app, int port)
        => app.MapWhen(context => context.Connection.LocalPort == port, grpc => grpc.Run(HandleAsync));

    private static async Task HandleAsync(HttpContext context)
    {
        var request = context.Request;
        if (!HttpMethods.IsPost(request.Method) || request.ContentType?.StartsWith("application/grpc", StringComparison.OrdinalIgnoreCase) != true)
        {
            // Not a gRPC call at all: the protocol says to answer with plain HTTP.
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }

        var encoding = request.Headers["grpc-encoding"].ToString();
        if (encoding is not ("" or "identity" or "gzip"))
        {
            context.Response.Headers["grpc-accept-encoding"] = SupportedEncodings;
            TrailersOnly(context, (int)RpcStatus.Unimplemented, $"grpc-encoding \"{encoding}\" isn't supported; use identity or gzip.");
            return;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        if (Timeout(request.Headers["grpc-timeout"].ToString()) is { } timeout)
        {
            deadline.CancelAfter(timeout);
        }

        try
        {
            var messages = await ReadMessagesAsync(request.Body, gzip: encoding == "gzip", deadline.Token);
            var mediator = context.RequestServices.GetRequiredService<IMediator>();
            var result = await mediator.Send(new InvokeRpcMock(request.Path.Value ?? "/", messages, Metadata(request.Headers)), deadline.Token);
            if (result.Response is { } response)
            {
                await WriteAsync(context, response);
            }
            else
            {
                TrailersOnly(context, (int)result.Status, result.Message);
            }
        }
        catch (FrameException ex)
        {
            TrailersOnly(context, ex.Status, ex.Message);
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            TrailersOnly(context, (int)RpcStatus.DeadlineExceeded, "The call's deadline passed.");
        }
    }

    /// <summary>The body's length-prefixed messages: a flag byte (1 = compressed), a big-endian length, the bytes.</summary>
    private static async Task<IReadOnlyList<byte[]>> ReadMessagesAsync(Stream body, bool gzip, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxRequestBytes)
            {
                throw new FrameException(ResourceExhausted, $"The request is larger than {MaxRequestBytes / (1024 * 1024)} MB.");
            }

            buffer.Write(chunk, 0, read);
        }

        var data = buffer.ToArray();
        var messages = new List<byte[]>();
        var offset = 0;
        while (offset < data.Length)
        {
            if (data.Length - offset < 5)
            {
                throw new FrameException((int)RpcStatus.Internal, "The request ends in the middle of a message's 5-byte prefix.");
            }

            var compressed = data[offset] == 1;
            var length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 1, 4));
            if (length > MaxMessageBytes)
            {
                throw new FrameException(ResourceExhausted, $"A request message of {length} bytes is over the {MaxMessageBytes / (1024 * 1024)} MB limit.");
            }

            if (data.Length - offset - 5 < length)
            {
                throw new FrameException((int)RpcStatus.Internal, $"The request ends in the middle of a {length}-byte message.");
            }

            var message = data.AsSpan(offset + 5, (int)length).ToArray();
            if (compressed && !gzip)
            {
                throw new FrameException((int)RpcStatus.Internal, "A message is marked compressed, but the call names no grpc-encoding.");
            }

            messages.Add(compressed ? Gunzip(message) : message);
            offset += 5 + (int)length;
        }

        return messages;
    }

    private static byte[] Gunzip(byte[] message)
    {
        try
        {
            using var input = new GZipStream(new MemoryStream(message), CompressionMode.Decompress);
            using var output = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = input.Read(chunk)) > 0)
            {
                if (output.Length + read > MaxMessageBytes)
                {
                    throw new FrameException(ResourceExhausted, $"A request message is over the {MaxMessageBytes / (1024 * 1024)} MB limit once decompressed.");
                }

                output.Write(chunk, 0, read);
            }

            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            throw new FrameException((int)RpcStatus.Internal, "A message marked gzip-compressed isn't valid gzip.");
        }
    }

    private static async Task WriteAsync(HttpContext context, byte[] message)
    {
        context.Response.ContentType = "application/grpc";
        var frame = new byte[5 + message.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(1), (uint)message.Length);
        message.CopyTo(frame, 5);
        await context.Response.Body.WriteAsync(frame, context.RequestAborted);
        context.Response.AppendTrailer("grpc-status", "0");
    }

    /// <summary>No message: the status goes in the headers, which end the stream.</summary>
    private static void TrailersOnly(HttpContext context, int status, string? message)
    {
        context.Response.ContentType = "application/grpc";
        context.Response.Headers["grpc-status"] = status.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(message))
        {
            // Percent-encoded, as the protocol requires for anything outside printable ASCII.
            context.Response.Headers["grpc-message"] = Uri.EscapeDataString(message);
        }
    }

    /// <summary>"100m", "5S", "1H"… — up to 8 digits and a unit (gRPC's grpc-timeout); null when absent or malformed.</summary>
    private static TimeSpan? Timeout(string header)
    {
        if (header.Length is < 2 or > 9 || !long.TryParse(header.AsSpan(0, header.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return header[^1] switch
        {
            'H' => TimeSpan.FromHours(value),
            'M' => TimeSpan.FromMinutes(value),
            'S' => TimeSpan.FromSeconds(value),
            'm' => TimeSpan.FromMilliseconds(value),
            'u' => TimeSpan.FromMicroseconds(value),
            'n' => TimeSpan.FromTicks(Math.Max(1, value / 100)),
            _ => null
        };
    }

    /// <summary>The caller's metadata: every header but HTTP/2's pseudo-headers, the protocol's own and gRPC's reserved "grpc-*".</summary>
    private static Dictionary<string, string> Metadata(IHeaderDictionary headers)
        => headers
            .Where(header => !header.Key.StartsWith(':') && !header.Key.StartsWith("grpc-", StringComparison.OrdinalIgnoreCase) && !ProtocolHeaders.Contains(header.Key))
            .ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase);

    /// <summary>A request the protocol itself can't make sense of, with the gRPC status to answer.</summary>
    private sealed class FrameException(int status, string message) : Exception(message)
    {
        public int Status { get; } = status;
    }
}
