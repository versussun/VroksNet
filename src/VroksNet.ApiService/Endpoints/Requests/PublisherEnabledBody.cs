namespace VroksNet.ApiService.Endpoints.Requests;

/// <summary>
/// Body of the publisher start/stop PUT. <see cref="Enabled"/> is nullable so a missing or
/// misspelled field is a 400 rather than silently meaning "stop".
/// </summary>
public sealed record PublisherEnabledBody(bool? Enabled);
