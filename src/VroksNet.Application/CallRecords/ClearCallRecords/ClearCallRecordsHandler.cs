using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.CallRecords.ClearCallRecords;

public sealed class ClearCallRecordsHandler(ICallRecordRepository callRecords) : IRequestHandler<ClearCallRecords, int>
{
    public async ValueTask<int> Handle(ClearCallRecords request, CancellationToken cancellationToken)
        => await callRecords.DeleteAllAsync(cancellationToken);
}
