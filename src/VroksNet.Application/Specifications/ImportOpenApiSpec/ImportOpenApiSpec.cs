using Mediator;

namespace VroksNet.Application.Specifications.ImportOpenApiSpec;

public sealed record ImportOpenApiSpec(string FileName, string YamlContent) : IRequest<Guid>;
