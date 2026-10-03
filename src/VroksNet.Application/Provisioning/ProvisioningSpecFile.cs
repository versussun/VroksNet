using VroksNet.Domain.ApiSpecifications;

namespace VroksNet.Application.Provisioning;

/// <summary>A spec found under <c>specs/</c>, with its kind detected from the document's root key.</summary>
public sealed record ProvisioningSpecFile(string RelativePath, SpecificationKind Kind, string Content);
