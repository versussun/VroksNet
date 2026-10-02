using Mediator;

namespace VroksNet.Application.CallRecords.GetCallRecord;

/// <summary>One record's request/response bodies — what the history list leaves out. Null if no record with <see cref="Id"/> exists.</summary>
public sealed record GetCallRecord(Guid Id) : IRequest<CallRecordDetails?>;
