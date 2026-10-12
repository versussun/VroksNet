namespace VroksNet.Application.Abstractions;

/// <summary>A gRPC method's request and response message types, by full name (e.g. "shop.orders.v1.GetOrderRequest").</summary>
public sealed record RpcMethodTypes(string RequestType, string ResponseType);
