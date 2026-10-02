namespace VroksNet.ApiService.Endpoints.Requests;

/// <summary>
/// Body of the enable/disable PUT. <see cref="Enabled"/> is nullable so a missing or misspelled
/// field is a 400 rather than silently meaning "turn it off".
/// </summary>
public sealed record EndpointEnabledBody(bool? Enabled);
