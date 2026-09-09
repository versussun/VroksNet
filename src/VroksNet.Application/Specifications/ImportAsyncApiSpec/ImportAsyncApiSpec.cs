using Mediator;

namespace VroksNet.Application.Specifications.ImportAsyncApiSpec;

public sealed record ImportAsyncApiSpec(string FileName, string YamlContent) : IRequest<Guid>;
