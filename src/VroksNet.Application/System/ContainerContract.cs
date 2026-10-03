namespace VroksNet.Application.System;

/// <summary>
/// The container contract (docs/container-contract.md) this build implements. Bumped only on a
/// breaking change to it; it matches the image's <c>io.vroksnet.contract.version</c> label.
/// </summary>
public static class ContainerContract
{
    public const int Version = 1;
}
