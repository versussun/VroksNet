namespace VroksNet.Application.Abstractions;

/// <summary>
/// One broker option a connection type accepts (ADR 0003), as its broker adapter declares it —
/// enough for the Admin UI to build the input without knowing the broker.
/// </summary>
/// <param name="Name">The key in <c>brokerOptions</c>, e.g. <c>exchange</c>.</param>
/// <param name="Label">The input's label.</param>
/// <param name="SendDescription">What it does for a Send (a Send scenario or a Publisher), including what blank means; null if it doesn't apply to Send.</param>
/// <param name="SendPlaceholder">What a blank input shows for a Send; null if it doesn't apply to Send.</param>
/// <param name="ListenDescription">What it does for a Listen scenario; null if it doesn't apply to Listen.</param>
/// <param name="ListenPlaceholder">What a blank input shows for a Listen; null if it doesn't apply to Listen.</param>
/// <param name="SuggestedValue">What a new Test Scenario's input starts with, if anything.</param>
/// <param name="AllowedValues">The only values it takes (matched case-insensitively, stored as listed); null for any value.</param>
public sealed record BrokerOptionDefinition(
    string Name,
    string Label,
    string? SendDescription,
    string? SendPlaceholder,
    string? ListenDescription = null,
    string? ListenPlaceholder = null,
    string? SuggestedValue = null,
    IReadOnlyList<string>? AllowedValues = null);
