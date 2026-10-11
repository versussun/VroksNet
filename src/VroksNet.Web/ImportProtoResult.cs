namespace VroksNet.Web;

/// <summary>What a .proto import stored (POST /api/specifications/proto): one specification per file with services, and the files used only as imports.</summary>
public sealed record ImportProtoResult(ImportedProtoSpecification[] Specifications, string[] ImportOnlyFiles);
