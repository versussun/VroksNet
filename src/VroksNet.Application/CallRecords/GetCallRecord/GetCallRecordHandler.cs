using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.CallRecords.GetCallRecord;

public sealed class GetCallRecordHandler(ICallRecordRepository callRecords) : IRequestHandler<GetCallRecord, CallRecordDetails?>
{
    public async ValueTask<CallRecordDetails?> Handle(GetCallRecord request, CancellationToken cancellationToken)
    {
        var record = await callRecords.FindByIdAsync(request.Id, cancellationToken);
        return record is null ? null : new CallRecordDetails(record.Id, record.RequestSnapshot, record.ResponseSnapshot);
    }
}
