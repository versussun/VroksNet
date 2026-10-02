namespace VroksNet.Application.Abstractions;

/// <summary>Id → display name maps from <see cref="ICallRecordNameResolver"/>: specification titles, operation keys, connection names, scenario names, publisher names.</summary>
public sealed record CallRecordNames(
    IReadOnlyDictionary<Guid, string> SpecificationTitles,
    IReadOnlyDictionary<Guid, string> OperationKeys,
    IReadOnlyDictionary<Guid, string> ConnectionNames,
    IReadOnlyDictionary<Guid, string> TestScenarioNames,
    IReadOnlyDictionary<Guid, string> PublisherNames);
