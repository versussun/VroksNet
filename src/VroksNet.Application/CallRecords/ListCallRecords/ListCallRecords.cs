using Mediator;
using VroksNet.Domain.CallRecords;

namespace VroksNet.Application.CallRecords.ListCallRecords;

/// <summary>
/// One page of the call history, newest first. <see cref="Cursor"/> is the previous page's
/// <see cref="CallRecordPage.NextCursor"/> (null for the first page); <see cref="Limit"/> defaults
/// to 50 and is capped at 200.
/// </summary>
public sealed record ListCallRecords(
    Guid? SpecificationId = null,
    Guid? MockEndpointId = null,
    Guid? TestScenarioId = null,
    Guid? PublisherId = null,
    CallDirection? Direction = null,
    bool? ContractValid = null,
    string? Cursor = null,
    int? Limit = null) : IRequest<CallRecordPage>;
