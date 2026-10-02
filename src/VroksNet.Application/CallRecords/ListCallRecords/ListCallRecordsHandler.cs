using System.Text.Json;
using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.CallRecords;

namespace VroksNet.Application.CallRecords.ListCallRecords;

public sealed class ListCallRecordsHandler(
    ICallRecordRepository callRecords,
    ICallRecordNameResolver names) : IRequestHandler<ListCallRecords, CallRecordPage>
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    public async ValueTask<CallRecordPage> Handle(ListCallRecords request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit ?? DefaultLimit, 1, MaxLimit);
        var after = request.Cursor is null ? null : CallRecordCursorFormat.Parse(request.Cursor);
        var filter = new CallRecordFilter(request.SpecificationId, request.MockEndpointId, request.TestScenarioId, request.PublisherId, request.Direction, request.ContractValid);

        // One extra row tells whether an older page exists without a separate count query.
        var records = await callRecords.ListAsync(filter, after, limit + 1, cancellationToken);
        var page = records.Take(limit).ToList();
        if (page.Count == 0)
        {
            return new CallRecordPage([], null);
        }

        var resolved = await names.ResolveAsync(
            IdsOf(page, record => record.SpecificationId),
            IdsOf(page, record => record.MockEndpointId),
            IdsOf(page, record => record.ConnectionId),
            IdsOf(page, record => record.TestScenarioId),
            IdsOf(page, record => record.PublisherId),
            cancellationToken);

        var items = page
            .Select(record => new CallRecordSummary(
                record.Id,
                record.Timestamp,
                record.Direction,
                record.SpecificationId,
                NameOf(record.SpecificationId, resolved.SpecificationTitles, "(deleted specification)"),
                record.MockEndpointId,
                NameOf(record.MockEndpointId, resolved.OperationKeys, "(deleted operation)"),
                record.ConnectionId,
                NameOf(record.ConnectionId, resolved.ConnectionNames, "(deleted connection)"),
                record.TestScenarioId,
                NameOf(record.TestScenarioId, resolved.TestScenarioNames, "(deleted test scenario)"),
                record.PublisherId,
                NameOf(record.PublisherId, resolved.PublisherNames, "(deleted publisher)"),
                record.RequestLine,
                record.StatusCode,
                record.ContractValid,
                ParseMessages(record.ValidationErrors),
                ParseMessages(record.Warnings)))
            .ToList();

        var last = page[^1];
        var nextCursor = records.Count > limit ? CallRecordCursorFormat.Format(new CallRecordCursor(last.Timestamp, last.Id)) : null;
        return new CallRecordPage(items, nextCursor);
    }

    private static Guid[] IdsOf(List<CallRecord> page, Func<CallRecord, Guid?> id)
        => page.Select(id).OfType<Guid>().Distinct().ToArray();

    private static string? NameOf(Guid? id, IReadOnlyDictionary<Guid, string> names, string deletedPlaceholder)
        => id is { } value ? names.GetValueOrDefault(value, deletedPlaceholder) : null;

    /// <summary><see cref="CallRecord.ValidationErrors"/>/<see cref="CallRecord.Warnings"/> are JSON string arrays written by the run/mock handlers; anything else is shown as-is rather than dropped.</summary>
    private static IReadOnlyList<string> ParseMessages(string? json)
    {
        if (json is null)
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [json];
        }
    }
}
