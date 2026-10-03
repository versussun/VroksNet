namespace VroksNet.Application.Provisioning;

/// <summary>How many objects of each kind provisioning applied.</summary>
public sealed record ProvisioningCounts(int Specifications, int Connections, int Publishers, int TestScenarios, int TestSuites = 0)
{
    public static ProvisioningCounts None { get; } = new(0, 0, 0, 0, 0);
}
