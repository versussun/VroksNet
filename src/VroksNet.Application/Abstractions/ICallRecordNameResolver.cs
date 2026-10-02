namespace VroksNet.Application.Abstractions;

/// <summary>
/// Looks up just the display names the call history needs for the ids on one page — instead of
/// loading every specification (with its endpoints' schemas), connection and scenario. Ids that
/// no longer exist are simply absent from the result.
/// </summary>
public interface ICallRecordNameResolver
{
    Task<CallRecordNames> ResolveAsync(
        IReadOnlyCollection<Guid> specificationIds,
        IReadOnlyCollection<Guid> mockEndpointIds,
        IReadOnlyCollection<Guid> connectionIds,
        IReadOnlyCollection<Guid> testScenarioIds,
        CancellationToken cancellationToken);
}
