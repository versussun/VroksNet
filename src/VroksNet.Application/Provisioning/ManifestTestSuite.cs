namespace VroksNet.Application.Provisioning;

/// <summary>A test suite to create or bring in line by <see cref="Name"/>; its scenarios by name, in order.</summary>
public sealed record ManifestTestSuite(string Name, IReadOnlyList<string> Scenarios, bool RunOnStartup);
