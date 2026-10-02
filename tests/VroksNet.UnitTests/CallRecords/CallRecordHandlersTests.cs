using VroksNet.Application.CallRecords.ClearCallRecords;
using VroksNet.Application.CallRecords.GetCallRecord;
using VroksNet.Application.CallRecords.ListCallRecords;
using VroksNet.Domain.CallRecords;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.CallRecords;

public class CallRecordHandlersTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeCallRecordRepository _callRecords = new();
    private readonly FakeCallRecordNameResolver _names = new();

    private ListCallRecordsHandler ListHandler => new(_callRecords, _names);

    [Fact]
    public async Task List_ResolvesNamesAndParsesValidationErrors()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var specificationId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var scenarioId = Guid.NewGuid();
        _names.SpecificationTitles[specificationId] = "Petstore";
        _names.OperationKeys[endpointId] = "GET /pets";
        _names.ConnectionNames[connectionId] = "Pets API";
        _names.TestScenarioNames[scenarioId] = "List pets";
        await _callRecords.InsertAsync(new CallRecord
        {
            Id = Guid.NewGuid(),
            Timestamp = Start,
            Direction = CallDirection.OutboundHttpRequest,
            SpecificationId = specificationId,
            MockEndpointId = endpointId,
            ConnectionId = connectionId,
            TestScenarioId = scenarioId,
            StatusCode = 200,
            ContractValid = false,
            ValidationErrors = """["Value is \"string\" but should be \"integer\""]"""
        }, cancellationToken);

        var page = await ListHandler.Handle(new ListCallRecords(), cancellationToken);

        var item = Assert.Single(page.Items);
        Assert.Equal("Petstore", item.SpecificationTitle);
        Assert.Equal("GET /pets", item.OperationKey);
        Assert.Equal("Pets API", item.ConnectionName);
        Assert.Equal("List pets", item.TestScenarioName);
        Assert.Equal(200, item.StatusCode);
        Assert.False(item.ContractValid);
        Assert.Equal("Value is \"string\" but should be \"integer\"", Assert.Single(item.ValidationErrors));
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task List_DeletedReferencesShowPlaceholders_AbsentOnesStayNull()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _callRecords.InsertAsync(new CallRecord
        {
            Id = Guid.NewGuid(),
            Timestamp = Start,
            Direction = CallDirection.OutboundBrokerPublish,
            SpecificationId = Guid.NewGuid(), // never stored — i.e. deleted since
            ConnectionId = Guid.NewGuid()
        }, cancellationToken);

        var item = Assert.Single((await ListHandler.Handle(new ListCallRecords(), cancellationToken)).Items);

        Assert.Equal("(deleted specification)", item.SpecificationTitle);
        Assert.Equal("(deleted connection)", item.ConnectionName);
        Assert.Null(item.OperationKey);
        Assert.Null(item.TestScenarioName);
        Assert.Empty(item.ValidationErrors);
    }

    [Fact]
    public async Task List_PagesNewestFirstWithACursorUntilExhausted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        for (var i = 0; i < 5; i++)
        {
            await _callRecords.InsertAsync(new CallRecord { Id = Guid.NewGuid(), Timestamp = Start.AddMinutes(i), Direction = CallDirection.InboundHttpRequest }, cancellationToken);
        }

        var first = await ListHandler.Handle(new ListCallRecords(Limit: 2), cancellationToken);
        var second = await ListHandler.Handle(new ListCallRecords(Cursor: first.NextCursor, Limit: 2), cancellationToken);
        var third = await ListHandler.Handle(new ListCallRecords(Cursor: second.NextCursor, Limit: 2), cancellationToken);

        Assert.Equal([Start.AddMinutes(4), Start.AddMinutes(3)], first.Items.Select(item => item.Timestamp));
        Assert.Equal([Start.AddMinutes(2), Start.AddMinutes(1)], second.Items.Select(item => item.Timestamp));
        Assert.Equal([Start], third.Items.Select(item => item.Timestamp));
        Assert.NotNull(first.NextCursor);
        Assert.Null(third.NextCursor);
    }

    [Fact]
    public async Task List_MalformedCursor_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await ListHandler.Handle(new ListCallRecords(Cursor: "not-a-cursor"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Get_ReturnsTheBodiesTheListLeavesOut_OrNullWhenUnknown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        await _callRecords.InsertAsync(new CallRecord { Id = id, Timestamp = Start, RequestSnapshot = "req", ResponseSnapshot = "resp" }, cancellationToken);
        var handler = new GetCallRecordHandler(_callRecords);

        var details = await handler.Handle(new GetCallRecord(id), cancellationToken);
        var missing = await handler.Handle(new GetCallRecord(Guid.NewGuid()), cancellationToken);

        Assert.NotNull(details);
        Assert.Equal("req", details.RequestSnapshot);
        Assert.Equal("resp", details.ResponseSnapshot);
        Assert.Null(missing);
    }

    [Fact]
    public async Task Clear_DeletesEverythingAndReturnsCount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _callRecords.InsertAsync(new CallRecord { Id = Guid.NewGuid(), Timestamp = Start }, cancellationToken);
        await _callRecords.InsertAsync(new CallRecord { Id = Guid.NewGuid(), Timestamp = Start }, cancellationToken);

        var deleted = await new ClearCallRecordsHandler(_callRecords).Handle(new ClearCallRecords(), cancellationToken);

        Assert.Equal(2, deleted);
        Assert.Empty(_callRecords.Inserted);
    }
}
