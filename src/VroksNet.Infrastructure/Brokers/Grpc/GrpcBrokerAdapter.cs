using System.Text.RegularExpressions;
using Grpc.Core;
using Grpc.Net.Client;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Infrastructure.Brokers.Grpc;

/// <summary>
/// <see cref="ConnectionServiceType.Grpc"/> (ADR 0004, decision 7): test and Send, no Listen. The
/// value is the server's URL, <c>http://</c> for HTTP/2 without TLS (h2c) or <c>https://</c>. Calls
/// go through <c>Grpc.Net.Client</c> with a <c>Method&lt;byte[], byte[]&gt;</c> and pass-through
/// marshallers, so no generated client is needed: Send writes the payload's JSON as the request
/// message through <see cref="IProtobufTranscoder"/> and hands back the response message's bytes for
/// the caller to decode and check. The check calls <c>grpc.health.v1.Health/Check</c>; any status a
/// server answers with, UNIMPLEMENTED included, proves it's reachable — like the Http check, where
/// any HTTP status does.
/// </summary>
public sealed partial class GrpcBrokerAdapter(IProtobufTranscoder transcoder) : IBrokerAdapter
{
    private const string HealthCheck = "/grpc.health.v1.Health/Check";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);
    private static readonly Marshaller<byte[]> PassThrough = Marshallers.Create(bytes => bytes, bytes => bytes);

    public ConnectionServiceType Type => ConnectionServiceType.Grpc;

    public IReadOnlyList<BrokerOptionDefinition> Options => [];

    public async Task<ConnectionTestResult> TestAsync(Connection connection, CancellationToken cancellationToken)
    {
        if (AddressOf(connection.Value, out var problem) is not { } address)
        {
            return new ConnectionTestResult(false, problem);
        }

        try
        {
            // An empty HealthCheckRequest asks about the server as a whole.
            await CallAsync(address, HealthCheck, [], TestTimeout, cancellationToken);
            return new ConnectionTestResult(true, $"Reached {address.Host} — its health check answered OK.");
        }
        catch (RpcException ex) when (IsUnreachable(ex))
        {
            return new ConnectionTestResult(false, ex.Status.DebugException?.Message ?? ex.Status.Detail);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
        {
            return new ConnectionTestResult(false, $"Timed out after {TestTimeout.TotalSeconds:0}s.");
        }
        catch (RpcException ex)
        {
            return new ConnectionTestResult(true, $"Reached {address.Host} — it answered {NameOf(ex.StatusCode)}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionTestResult(false, ex.Message);
        }
    }

    public async Task<MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, BrokerOptions? options, CancellationToken cancellationToken, RpcSendContext? rpc = null)
    {
        if (OperationCompatibility.ShapeOf(operationKey) != OperationShape.Rpc)
        {
            return new MessageSendResult(false, "This operation isn't a gRPC method (expected \"RPC /package.Service/Method\") — it can't be sent to a gRPC connection.");
        }

        if (rpc is null)
        {
            return new MessageSendResult(false, "The method's message types are unknown — import its .proto file again.");
        }

        if (AddressOf(connection.Value, out var problem) is not { } address)
        {
            return new MessageSendResult(false, problem);
        }

        var request = transcoder.ToBinary(rpc.DescriptorSet, rpc.Types.RequestType, payload ?? "{}");
        if (request.Bytes is null)
        {
            return new MessageSendResult(false, $"The payload isn't a valid {rpc.Types.RequestType}: {request.Error}");
        }

        try
        {
            var response = await CallAsync(address, operationKey[OperationCompatibility.RpcPrefix.Length..], request.Bytes, SendTimeout, cancellationToken);
            return new MessageSendResult(true, NameOf(StatusCode.OK), GrpcStatus: (int)StatusCode.OK, ResponseMessage: response);
        }
        catch (RpcException ex) when (IsUnreachable(ex))
        {
            // Nothing answered, so there's no status to record.
            return new MessageSendResult(false, ex.Status.DebugException?.Message ?? ex.Status.Detail);
        }
        catch (RpcException ex)
        {
            var message = ex.StatusCode == StatusCode.DeadlineExceeded
                ? $"{NameOf(ex.StatusCode)}: no answer within {SendTimeout.TotalSeconds:0}s."
                : string.IsNullOrWhiteSpace(ex.Status.Detail) ? NameOf(ex.StatusCode) : $"{NameOf(ex.StatusCode)}: {ex.Status.Detail}";
            return new MessageSendResult(false, message, GrpcStatus: (int)ex.StatusCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MessageSendResult(false, ex.Message);
        }
    }

    /// <summary>
    /// The connection's URL; null, with <paramref name="problem"/> saying why, when it isn't an
    /// http(s) URL or has a path — gRPC calls "/package.Service/Method" at the server's root, and
    /// clients can't put a base path in front of it.
    /// </summary>
    private static Uri? AddressOf(string value, out string problem)
    {
        problem = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            problem = "Not a valid http:// or https:// URL.";
            return null;
        }

        if (uri.AbsolutePath != "/" || uri.Query.Length > 0)
        {
            problem = "A gRPC URL has no path or query — methods are called at the server's root (/package.Service/Method).";
            return null;
        }

        return uri;
    }

    /// <summary>One unary call on a channel of its own, with a deadline covering connecting too.</summary>
    private static async Task<byte[]> CallAsync(Uri address, string methodPath, byte[] request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var parts = methodPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            throw new ArgumentException($"\"{methodPath}\" isn't a gRPC method path (/package.Service/Method).");
        }

        using var channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler { ConnectTimeout = timeout },
            DisposeHttpClient = true,
            // A cancelled run surfaces as OperationCanceledException, not as a CANCELLED status.
            ThrowOperationCanceledOnCancellation = true
        });
        var method = new Method<byte[], byte[]>(MethodType.Unary, parts[0], parts[1], PassThrough, PassThrough);
        using var call = channel.CreateCallInvoker().AsyncUnaryCall(method, null, new CallOptions(deadline: DateTime.UtcNow.Add(timeout), cancellationToken: cancellationToken), request);
        return await call.ResponseAsync;
    }

    /// <summary>The call never reached a server: the client reports UNAVAILABLE with the transport's own exception.</summary>
    private static bool IsUnreachable(RpcException ex) => ex.StatusCode == StatusCode.Unavailable && ex.Status.DebugException is not null;

    /// <summary>A status as gRPC spells it: "NOT_FOUND", "DEADLINE_EXCEEDED".</summary>
    public static string NameOf(StatusCode status)
        => status == StatusCode.OK ? "OK" : WordBoundary().Replace(status.ToString(), "_").ToUpperInvariant();

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex WordBoundary();
}
