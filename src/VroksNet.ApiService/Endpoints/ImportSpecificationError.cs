namespace VroksNet.ApiService.Endpoints;

/// <summary>The 400 body returned when a spec file fails to parse — see SpecificationEndpoints.</summary>
public sealed record ImportSpecificationError(string Message);
