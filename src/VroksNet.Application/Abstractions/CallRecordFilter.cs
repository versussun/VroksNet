using VroksNet.Domain.CallRecords;

namespace VroksNet.Application.Abstractions;

/// <summary>Narrows a <see cref="ICallRecordRepository.ListAsync"/> page; every null criterion matches everything.</summary>
public sealed record CallRecordFilter(
    Guid? SpecificationId = null,
    Guid? MockEndpointId = null,
    Guid? TestScenarioId = null,
    Guid? PublisherId = null,
    CallDirection? Direction = null,
    bool? ContractValid = null);
