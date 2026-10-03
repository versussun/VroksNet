using VroksNet.Application.Abstractions;
using VroksNet.Application.TestRuns;
using VroksNet.Application.TestRuns.CancelTestRun;
using VroksNet.Application.TestRuns.ExecuteTestRun;
using VroksNet.Application.TestRuns.StartTestRun;
using VroksNet.Application.TestScenarios.RunTestScenario;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.Connections;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Domain.TestRuns;
using VroksNet.Domain.TestScenarios;
using VroksNet.Infrastructure.SchemaValidation;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.TestRuns;

/// <summary>
/// The run lifecycle (ADR 0002) around the shared executor: background execution, cancelling a
/// queued or running run, shutdown, and the synchronous run that now records a run too.
/// </summary>
public sealed class TestRunHandlersTests
{
    private readonly FakeTestScenarioRepository _scenarios = new();
    private readonly FakeApiSpecificationRepository _specifications = new();
    private readonly FakeConnectionRepository _connections = new();
    private readonly FakeCallRecordRepository _callRecords = new();
    private readonly FakeTestRunRepository _runs = new();
    private readonly TestRunCancellations _cancellations = new();
    private readonly Guid _specificationId = Guid.NewGuid();
    private readonly Guid _httpEndpointId = Guid.NewGuid();
    private readonly Guid _channelEndpointId = Guid.NewGuid();
    private readonly Guid _httpConnectionId = Guid.NewGuid();
    private readonly Guid _natsConnectionId = Guid.NewGuid();

    [Fact]
    public async Task StartThenExecute_RunsTheScenario_AndRecordsAPassedRun()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenarioId = await ArrangeHttpScenarioAsync();

        var runId = await new StartTestRunHandler(_scenarios, _runs, TimeProvider.System).Handle(new StartTestRun(scenarioId), cancellationToken);
        Assert.NotNull(runId);
        Assert.Equal(TestRunStatus.Queued, _runs.All.Single().Status);

        await ExecuteHandler(new FakeMessageSender(new MessageSendResult(true, "200 OK", "[]", 200))).Handle(new ExecuteTestRun(runId.Value), cancellationToken);

        var run = _runs.All.Single();
        Assert.Equal(TestRunStatus.Passed, run.Status);
        Assert.NotNull(run.StartedAt);
        Assert.NotNull(run.FinishedAt);
        Assert.Equal(200, run.StatusCode);
        Assert.Equal(runId, Assert.Single(_callRecords.Inserted).TestRunId);
    }

    [Fact]
    public async Task StartTestRun_Delayed_QueuesItForLater_AsDelayed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenarioId = await ArrangeHttpScenarioAsync();
        var now = DateTimeOffset.Parse("2026-10-03T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var start = new StartTestRunHandler(_scenarios, _runs, new FixedTimeProvider(now));

        var inTenMinutes = (await start.Handle(new StartTestRun(scenarioId, DelaySeconds: 600), cancellationToken))!.Value;
        var atThreeKyiv = (await start.Handle(new StartTestRun(scenarioId, RunAt: DateTimeOffset.Parse("2026-10-03T15:00:00+03:00", System.Globalization.CultureInfo.InvariantCulture)), cancellationToken))!.Value;

        var delayed = _runs.All.Single(run => run.Id == inTenMinutes);
        Assert.Equal((TestRunStatus.Queued, TestRunTrigger.Delayed, now.AddMinutes(10)), (delayed.Status, delayed.Trigger, delayed.ScheduledFor));
        var scheduled = _runs.All.Single(run => run.Id == atThreeKyiv);
        Assert.Equal(TestRunTrigger.Delayed, scheduled.Trigger);
        Assert.Equal(DateTimeOffset.Parse("2026-10-03T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture), scheduled.ScheduledFor);
        Assert.Equal(TimeSpan.Zero, scheduled.ScheduledFor.Offset);

        // Not due yet, so the worker leaves both alone until then.
        Assert.Empty(await _runs.ListDueAsync(now.AddMinutes(5), cancellationToken));
        Assert.Equal(inTenMinutes, Assert.Single(await _runs.ListDueAsync(now.AddMinutes(10), cancellationToken)).Id);
    }

    [Theory]
    [InlineData("2026-10-03T10:05:00Z", 300, "not both")]
    [InlineData(null, 0, "delaySeconds must be from 1")]
    [InlineData(null, 2_592_001, "delaySeconds must be from 1")]
    [InlineData("2026-10-03T09:59:59Z", null, "has already passed")]
    [InlineData("2026-11-03T10:00:00Z", null, "at most 30 days ahead")]
    public async Task StartTestRun_BadDelay_ThrowsWithTheReason(string? runAt, int? delaySeconds, string reason)
    {
        var scenarioId = await ArrangeHttpScenarioAsync();
        var start = new StartTestRunHandler(_scenarios, _runs, new FixedTimeProvider(DateTimeOffset.Parse("2026-10-03T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture)));
        var request = new StartTestRun(scenarioId, runAt is null ? null : DateTimeOffset.Parse(runAt, System.Globalization.CultureInfo.InvariantCulture), delaySeconds);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => start.Handle(request, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(reason, ex.Message);
        Assert.Empty(_runs.All);
    }

    [Fact]
    public async Task StartTestRun_UnknownScenario_ReturnsNull()
    {
        var runId = await new StartTestRunHandler(_scenarios, _runs, TimeProvider.System).Handle(new StartTestRun(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Null(runId);
        Assert.Empty(_runs.All);
    }

    [Fact]
    public async Task CancelTestRun_Queued_CancelsIt_AndExecuteThenLeavesItAlone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenarioId = await ArrangeHttpScenarioAsync();
        var runId = (await new StartTestRunHandler(_scenarios, _runs, TimeProvider.System).Handle(new StartTestRun(scenarioId), cancellationToken))!.Value;

        Assert.Equal(CancelTestRunResult.Cancelled, await CancelHandler.Handle(new CancelTestRun(runId), cancellationToken));
        var sender = new FakeMessageSender(new MessageSendResult(true, "200 OK"));
        await ExecuteHandler(sender).Handle(new ExecuteTestRun(runId), cancellationToken);

        Assert.Equal(TestRunStatus.Cancelled, _runs.All.Single().Status);
        Assert.Null(sender.LastSend);
        Assert.Equal(CancelTestRunResult.NotActive, await CancelHandler.Handle(new CancelTestRun(runId), cancellationToken));
        Assert.Equal(CancelTestRunResult.NotFound, await CancelHandler.Handle(new CancelTestRun(Guid.NewGuid()), cancellationToken));
    }

    [Fact]
    public async Task CancelTestRun_Running_StopsTheListen_AndTheRunEndsCancelled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenarioId = await ArrangeListenScenarioAsync(timeoutSeconds: 1800);
        var runId = (await new StartTestRunHandler(_scenarios, _runs, TimeProvider.System).Handle(new StartTestRun(scenarioId), cancellationToken))!.Value;

        var execution = ExecuteHandler(listener: new BlockingMessageListener()).Handle(new ExecuteTestRun(runId), cancellationToken).AsTask();
        await WaitUntilAsync(() => _runs.All.Single().Status == TestRunStatus.Running);

        Assert.Equal(CancelTestRunResult.Cancelled, await CancelHandler.Handle(new CancelTestRun(runId), cancellationToken));
        await execution.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        Assert.Equal(TestRunStatus.Cancelled, _runs.All.Single().Status);
    }

    [Fact]
    public async Task ExecuteTestRun_Shutdown_EndsTheRunInterrupted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenarioId = await ArrangeListenScenarioAsync(timeoutSeconds: 1800);
        var runId = (await new StartTestRunHandler(_scenarios, _runs, TimeProvider.System).Handle(new StartTestRun(scenarioId), cancellationToken))!.Value;
        using var shutdown = new CancellationTokenSource();

        var execution = ExecuteHandler(listener: new BlockingMessageListener()).Handle(new ExecuteTestRun(runId), shutdown.Token).AsTask();
        await WaitUntilAsync(() => _runs.All.Single().Status == TestRunStatus.Running);
        await shutdown.CancelAsync();
        await execution.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        Assert.Equal(TestRunStatus.Interrupted, _runs.All.Single().Status);
    }

    [Fact]
    public async Task ExecuteTestRun_DeletedScenario_FailsTheRun()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenarioId = await ArrangeHttpScenarioAsync();
        var runId = (await new StartTestRunHandler(_scenarios, _runs, TimeProvider.System).Handle(new StartTestRun(scenarioId), cancellationToken))!.Value;
        await _scenarios.DeleteAsync(scenarioId, cancellationToken);

        await ExecuteHandler().Handle(new ExecuteTestRun(runId), cancellationToken);

        var run = _runs.All.Single();
        Assert.Equal(TestRunStatus.Failed, run.Status);
        Assert.Equal("The scenario no longer exists.", run.Message);
    }

    [Fact]
    public async Task RunTestScenario_Synchronous_RecordsAManualRun()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenarioId = await ArrangeHttpScenarioAsync();
        var handler = TestRunHandlers.Run(
            _scenarios, _specifications, _connections, new FakeMessageSender(new MessageSendResult(true, "200 OK", "[]", 200)),
            new FakeMessageListener(new MessageListenResult(false, "unused")), new SchemaValidator(), _callRecords, _runs, _cancellations);

        var result = await handler.Handle(new RunTestScenario(scenarioId), cancellationToken);

        Assert.True(result!.Success);
        var run = _runs.All.Single();
        Assert.Equal(TestRunTrigger.Manual, run.Trigger);
        Assert.Equal(TestRunStatus.Passed, run.Status);
        Assert.Equal(run.Id, Assert.Single(_callRecords.Inserted).TestRunId);
    }

    [Fact]
    public async Task RunTestScenario_ListenLongerThanASynchronousRunAllows_IsRefusedWithoutRunning()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenarioId = await ArrangeListenScenarioAsync(timeoutSeconds: TestScenarioListening.MaxSynchronousTimeoutSeconds + 1);
        var listener = new FakeMessageListener(new MessageListenResult(true, "should not be called"));
        var handler = TestRunHandlers.Run(
            _scenarios, _specifications, _connections, new FakeMessageSender(new MessageSendResult(false, "unused")),
            listener, new SchemaValidator(), _callRecords, _runs, _cancellations);

        var result = await handler.Handle(new RunTestScenario(scenarioId), cancellationToken);

        Assert.False(result!.Success);
        Assert.Contains("Run it in the background", result.Message);
        Assert.Empty(_runs.All);
        Assert.Empty(_callRecords.Inserted);
    }

    private CancelTestRunHandler CancelHandler => new(_runs, _cancellations, TimeProvider.System);

    private ExecuteTestRunHandler ExecuteHandler(IMessageSender? sender = null, IMessageListener? listener = null) => new(
        _runs,
        new TestRunRunner(
            _runs,
            _scenarios,
            TestRunHandlers.Executor(
                _scenarios, _specifications, _connections,
                sender ?? new FakeMessageSender(new MessageSendResult(true, "200 OK")),
                listener ?? new FakeMessageListener(new MessageListenResult(false, "unused")),
                new SchemaValidator(), _callRecords),
            _cancellations,
            TimeProvider.System));

    private async Task<Guid> ArrangeHttpScenarioAsync() => await ArrangeScenarioAsync(_httpEndpointId, _httpConnectionId, TestScenarioKind.Send, null);

    private async Task<Guid> ArrangeListenScenarioAsync(int timeoutSeconds) => await ArrangeScenarioAsync(_channelEndpointId, _natsConnectionId, TestScenarioKind.Listen, timeoutSeconds);

    private async Task<Guid> ArrangeScenarioAsync(Guid endpointId, Guid connectionId, TestScenarioKind kind, int? timeoutSeconds)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _specifications.UpsertAsync(new ApiSpecification
        {
            Id = _specificationId,
            Title = "Shop",
            Kind = SpecificationKind.OpenApi,
            Endpoints =
            [
                new MockEndpoint { Id = _httpEndpointId, SpecificationId = _specificationId, OperationKey = "GET /pets", ExampleTemplate = "[]" },
                new MockEndpoint { Id = _channelEndpointId, SpecificationId = _specificationId, OperationKey = "orders.created:send" }
            ]
        }, cancellationToken);
        await _connections.InsertAsync(new Connection { Id = _httpConnectionId, Name = "API", ServiceType = ConnectionServiceType.Http, Value = "https://api.example.com" }, cancellationToken);
        await _connections.InsertAsync(new Connection { Id = _natsConnectionId, Name = "NATS", ServiceType = ConnectionServiceType.Nats, Value = "nats://localhost:4222" }, cancellationToken);

        var scenarioId = Guid.NewGuid();
        await _scenarios.InsertAsync(new TestScenario
        {
            Id = scenarioId,
            Name = "Scenario",
            SpecificationId = _specificationId,
            MockEndpointId = endpointId,
            ConnectionId = connectionId,
            Kind = kind,
            ListenTimeoutSeconds = timeoutSeconds
        }, cancellationToken);
        return scenarioId;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    /// <summary>A broker listen that never receives anything and only ends when cancelled, like a real long Listen.</summary>
    private sealed class BlockingMessageListener : IMessageListener
    {
        public async Task<MessageListenResult> ListenAsync(Connection connection, string operationKey, TimeSpan timeout, BrokerOptions? options, CancellationToken cancellationToken, Action? onListening = null)
        {
            onListening?.Invoke();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new MessageListenResult(false, "unreachable");
        }
    }
}
