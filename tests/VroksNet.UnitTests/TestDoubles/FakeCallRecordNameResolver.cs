using VroksNet.Application.Abstractions;

namespace VroksNet.UnitTests.TestDoubles;

/// <summary>Answers from whatever names a test registered; anything else is "not found" (i.e. deleted).</summary>
internal sealed class FakeCallRecordNameResolver : ICallRecordNameResolver
{
    public Dictionary<Guid, string> SpecificationTitles { get; } = [];

    public Dictionary<Guid, string> OperationKeys { get; } = [];

    public Dictionary<Guid, string> ConnectionNames { get; } = [];

    public Dictionary<Guid, string> TestScenarioNames { get; } = [];

    public Task<CallRecordNames> ResolveAsync(
        IReadOnlyCollection<Guid> specificationIds,
        IReadOnlyCollection<Guid> mockEndpointIds,
        IReadOnlyCollection<Guid> connectionIds,
        IReadOnlyCollection<Guid> testScenarioIds,
        CancellationToken cancellationToken)
        => Task.FromResult(new CallRecordNames(
            Pick(SpecificationTitles, specificationIds),
            Pick(OperationKeys, mockEndpointIds),
            Pick(ConnectionNames, connectionIds),
            Pick(TestScenarioNames, testScenarioIds)));

    private static Dictionary<Guid, string> Pick(Dictionary<Guid, string> names, IReadOnlyCollection<Guid> ids)
        => ids.Where(names.ContainsKey).ToDictionary(id => id, id => names[id]);
}
