using VroksNet.Domain.Connections;

namespace VroksNet.Application.Provisioning;

/// <summary>A connection to create or bring in line by <see cref="Name"/>. <see cref="Value"/> is resolved — never echo it in errors.</summary>
public sealed record ManifestConnection(string Name, ConnectionServiceType Type, string Value);
