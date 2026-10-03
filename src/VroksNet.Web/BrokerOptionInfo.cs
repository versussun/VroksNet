namespace VroksNet.Web;

/// <summary>
/// One broker option a connection type accepts, as its broker adapter describes it (part of GET
/// /api/system/connection-types, ADR 0003). The forms build their inputs from these, so a new
/// option needs no Web change. <see cref="ListenDescription"/> is null when it doesn't apply to Listen.
/// </summary>
public sealed record BrokerOptionInfo(
    string Name,
    string Label,
    string SendDescription,
    string SendPlaceholder,
    string? ListenDescription,
    string? ListenPlaceholder,
    string? SuggestedValue);
