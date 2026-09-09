namespace VroksNet.Web;

/// <summary>Mirrors VroksNet.ApiService.Endpoints.ImportSpecificationError — the 400 body a failed spec-file parse returns.</summary>
public sealed record ImportSpecificationError(string Message);
