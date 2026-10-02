namespace VroksNet.Application.Mocking.SetEndpointProviderMode;

/// <summary><see cref="Refusal"/> is set (UI-safe) when <see cref="Found"/> but the change was refused — a non-HTTP operation, or an overlap with an operation already served.</summary>
public sealed record SetEndpointProviderModeResult(bool Found, string? Refusal = null)
{
    public bool Succeeded => Found && Refusal is null;
}
