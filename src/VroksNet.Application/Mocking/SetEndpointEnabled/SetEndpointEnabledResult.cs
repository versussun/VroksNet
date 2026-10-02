namespace VroksNet.Application.Mocking.SetEndpointEnabled;

/// <summary><see cref="Refusal"/> is set (UI-safe) when <see cref="Found"/> but the change was refused — a non-HTTP operation, or one replaced meanwhile.</summary>
public sealed record SetEndpointEnabledResult(bool Found, string? Refusal = null)
{
    public bool Succeeded => Found && Refusal is null;
}
