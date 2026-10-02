namespace VroksNet.Application.Abstractions;

/// <summary>Id → display name maps from <see cref="ICallRecordNameResolver"/>: specification titles, operation keys, connection names, scenario names.</summary>
public sealed record CallRecordNames(
    IReadOnlyDictionary<Guid, string> SpecificationTitles,
    IReadOnlyDictionary<Guid, string> OperationKeys,
    IReadOnlyDictionary<Guid, string> ConnectionNames,
    IReadOnlyDictionary<Guid, string> TestScenarioNames);
