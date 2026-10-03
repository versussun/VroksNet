using VroksNet.Domain.Connections;

namespace VroksNet.Application.System.ListConnectionTypes;

/// <summary>
/// One <see cref="ConnectionServiceType"/> as <c>GET /api/system/connection-types</c> describes it
/// (<see cref="ServiceTypeTraits"/>): what to call it, what its value looks like, and what it can do.
/// The Admin UI builds its type list, placeholders and connection filters from these.
/// </summary>
public sealed record ConnectionTypeInfo(
    ConnectionServiceType Type,
    string DisplayName,
    string ValueLabel,
    string ValueHint,
    bool IsHttp,
    bool CanListen,
    string? ListenNote);
