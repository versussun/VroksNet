using System.Collections.Concurrent;
using VroksNet.Application.Abstractions;
using VroksNet.Application.TestRuns;
using VroksNet.Application.TestRuns.CancelTestRun;
using VroksNet.Application.TestSuites.CancelSuiteRun;
using VroksNet.Application.TestSuites.CreateTestSuite;
using VroksNet.Application.TestSuites.ExecuteSuiteRun;
using VroksNet.Application.TestSuites.GetSuiteRun;
using VroksNet.Application.TestSuites.StartSuiteRun;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.Connections;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Domain.TestRuns;
using VroksNet.Domain.TestScenarios;
using VroksNet.Infrastructure.SchemaValidation;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.TestSuites;

/// <summary>
/// Test suites (ADR 0002, step B4): a suite run starts every Listen first and its Sends only once
/// the Listens are subscribed, passes only if every run passed, and cancels as a whole.
/// </summary>
public sealed class SuiteRunTests
{
    private readonly FakeTestScenarioRepository _scenarios = new();
    private readonly FakeApiSpecificationRepository _specifications = new();
    private readonly FakeConnectionRepository _connections = new();
    private readonly FakeTestRunRepository _runs = new();
    private readonly FakeTestSuiteRepository _suites = new();
    private readonly FakeSuiteRunRepository _suiteRuns = new();
    private readonly TestRunCancellations _cancellations = new();
    private readonly Guid _specificationId = Guid.NewGuid();
    private readonly Guid _channelEndpointId = Guid.NewGuid();
    private readonly Guid _natsConnectionId = Guid.NewGuid();

    /// <summary>What happened, in order, across the fake broker's listens and sends.</summary>
    private readonly ConcurrentQueue<string> _events = new();

    [Fact]
    public async Task Listens_AreSubscribedBeforeAnySendStarts_AndTheSuitePasses()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ArrangeAsync();
        var send = await AddScenarioAsync("send-order", TestScenarioKind.Send);
        var listenA = await AddScenarioAsync("hear-order-a", TestScenarioKind.Listen);
        var listenB = await AddScenarioAsync("hear-order-b", TestScenarioKind.Listen);
        // The Send comes first in the suite, yet runs after both Listens are subscribed.
        var suiteRunId = await StartAsync("chain", [send, listenA, listenB]);
        var broker = new Broker(_events);

        await ExecuteHandler(broker, broker).Handle(new ExecuteSuiteRun(suiteRunId), cancellationToken);

        var order = _events.ToList();
        Assert.Equal(3, order.Count);
        Assert.Equal("send", order[^1]); // both "subscribed" came first
        var details = (await GetHandler.Handle(new GetSuiteRun(suiteRunId), cancellationToken))!;
        Assert.Equal(TestRunStatus.Passed, details.Status);
        Assert.Equal("All 3 passed.", details.Message);
        Assert.Empty(details.Failed);
        Assert.Equal(["send-order", "hear-order-a", "hear-order-b"], details.Runs.Select(run => run.ScenarioName)); // the suite's order
        Assert.All(_runs.All, run => Assert.Equal((TestRunTrigger.Suite, suiteRunId), (run.Trigger, run.SuiteRunId)));
    }

    [Fact]
    public async Task AListenThatFailsBeforeSubscribing_DoesntHoldUpTheSends_AndFailsTheSuite()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ArrangeAsync();
        var send = await AddScenarioAsync("send-order", TestScenarioKind.Send);
        var listen = await AddScenarioAsync("hear-order", TestScenarioKind.Listen);
        var suiteRunId = await StartAsync("broken-listen", [listen, send]);

        await ExecuteHandler(
            new FakeMessageSender(new MessageSendResult(true, "Published.")),
            new FakeMessageListener(new MessageListenResult(false, "Timed out connecting to the broker after 10s."))).Handle(new ExecuteSuiteRun(suiteRunId), cancellationToken);

        var details = (await GetHandler.Handle(new GetSuiteRun(suiteRunId), cancellationToken))!;
        Assert.Equal(TestRunStatus.Failed, details.Status);
        Assert.Equal("1 of 2 passed. Failed: hear-order.", details.Message);
        Assert.Equal(["hear-order"], details.Failed);
        Assert.Equal(TestRunStatus.Passed, details.Runs.Single(run => run.ScenarioName == "send-order").Status);
    }

    [Fact]
    public async Task AScenarioDeletedSinceTheSuiteWasSaved_FailsItsRun()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ArrangeAsync();
        var kept = await AddScenarioAsync("kept", TestScenarioKind.Send);
        var deleted = await AddScenarioAsync("deleted", TestScenarioKind.Send);
        var suiteRunId = await StartAsync("with-a-gap", [kept, deleted]);
        await _scenarios.DeleteAsync(deleted, cancellationToken);

        await ExecuteHandler(new FakeMessageSender(new MessageSendResult(true, "Published.")), new FakeMessageListener(new MessageListenResult(false, "unused")))
            .Handle(new ExecuteSuiteRun(suiteRunId), cancellationToken);

        var details = (await GetHandler.Handle(new GetSuiteRun(suiteRunId), cancellationToken))!;
        Assert.Equal(TestRunStatus.Failed, details.Status);
        Assert.Equal(["(deleted scenario)"], details.Failed);
        Assert.Equal("The scenario no longer exists.", details.Runs.Single(run => run.TestScenarioId == deleted).Message);
    }

    [Fact]
    public async Task CancellingARunningSuite_CancelsItsRuns_IncludingTheOnesNotStarted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ArrangeAsync();
        var listen = await AddScenarioAsync("hear-order", TestScenarioKind.Listen);
        var firstSend = await AddScenarioAsync("send-first", TestScenarioKind.Send);
        var secondSend = await AddScenarioAsync("send-second", TestScenarioKind.Send);
        var suiteRunId = await StartAsync("cancel-me", [listen, firstSend, secondSend]);
        var broker = new Broker(_events, blockSends: true, blockListens: true);

        var execution = ExecuteHandler(broker, broker).Handle(new ExecuteSuiteRun(suiteRunId), cancellationToken).AsTask();
        await WaitUntilAsync(() => _events.Contains("send"));
        var result = await new CancelSuiteRunHandler(_suiteRuns, _cancellations, TimeProvider.System).Handle(new CancelSuiteRun(suiteRunId), cancellationToken);
        await execution;

        Assert.Equal(CancelTestRunResult.Cancelled, result);
        Assert.Equal(TestRunStatus.Cancelled, _suiteRuns.All.Single().Status);
        Assert.All(_runs.All, run => Assert.True(run.Status == TestRunStatus.Cancelled, $"{run.TestScenarioId}: {run.Status} {run.Message}"));
        Assert.Equal("Cancelled with its suite run.", _runs.All.Single(run => run.TestScenarioId == secondSend).Message);
    }

    [Fact]
    public async Task CreatingASuite_ChecksTheNameAndTheScenarios()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ArrangeAsync();
        var scenario = await AddScenarioAsync("one", TestScenarioKind.Send);
        var create = new CreateTestSuiteHandler(_suites, _scenarios, TimeProvider.System);
        await create.Handle(new CreateTestSuite(" nightly ", [scenario]), cancellationToken);

        Assert.Equal("nightly", (await _suites.ListAsync(cancellationToken)).Single().Name);
        foreach (var (request, reason) in new (CreateTestSuite, string)[]
        {
            (new CreateTestSuite("nightly", [scenario]), "already exists"),
            (new CreateTestSuite("empty", []), "at least one test scenario"),
            (new CreateTestSuite("twice", [scenario, scenario]), "only be in a suite once"),
            (new CreateTestSuite("ghost", [Guid.Empty]), "No test scenario with id"),
            (new CreateTestSuite(" ", [scenario]), "needs a name")
        })
        {
            var ex = await Assert.ThrowsAsync<ArgumentException>(() => create.Handle(request, cancellationToken).AsTask());
            Assert.Contains(reason, ex.Message);
        }
    }

    private async Task ArrangeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _specifications.UpsertAsync(new ApiSpecification
        {
            Id = _specificationId,
            Title = "Shop events",
            Kind = SpecificationKind.AsyncApi,
            Endpoints = [new MockEndpoint { Id = _channelEndpointId, SpecificationId = _specificationId, OperationKey = "orders.created:send", ExampleTemplate = "{}" }]
        }, cancellationToken);
        await _connections.InsertAsync(new Connection { Id = _natsConnectionId, Name = "NATS", ServiceType = ConnectionServiceType.Nats, Value = "nats://localhost:4222" }, cancellationToken);
    }

    private async Task<Guid> AddScenarioAsync(string name, TestScenarioKind kind)
    {
        var id = Guid.NewGuid();
        await _scenarios.InsertAsync(new TestScenario
        {
            Id = id, Name = name, SpecificationId = _specificationId, MockEndpointId = _channelEndpointId, ConnectionId = _natsConnectionId, Kind = kind
        }, TestContext.Current.CancellationToken);
        return id;
    }

    private async Task<Guid> StartAsync(string suiteName, IReadOnlyList<Guid> scenarioIds)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await new CreateTestSuiteHandler(_suites, _scenarios, TimeProvider.System).Handle(new CreateTestSuite(suiteName, scenarioIds), cancellationToken);
        return (await new StartSuiteRunHandler(_suites, _suiteRuns, TimeProvider.System).Handle(new StartSuiteRun(suiteName), cancellationToken))!.Value;
    }

    private ExecuteSuiteRunHandler ExecuteHandler(IMessageSender sender, IMessageListener listener) => new(
        _suiteRuns, _suites, _scenarios, _runs,
        new TestRunRunner(
            _runs, _scenarios,
            TestRunHandlers.Executor(_scenarios, _specifications, _connections, sender, listener, new SchemaValidator(), new FakeCallRecordRepository()),
            _cancellations, TimeProvider.System),
        _cancellations, TimeProvider.System);

    private GetSuiteRunHandler GetHandler => new(_suiteRuns, _suites, _scenarios, _runs);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    /// <summary>
    /// A broker where a Listen subscribes after a short delay and then receives the first message a
    /// Send publishes — so a Send started too early would leave the Listen waiting. Optionally
    /// Sends and Listens just hang until cancelled.
    /// </summary>
    private sealed class Broker(ConcurrentQueue<string> events, bool blockSends = false, bool blockListens = false) : IMessageSender, IMessageListener
    {
        private readonly TaskCompletionSource<string> _published = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<MessageSendResult> SendAsync(Connection connection, string operationKey, string? payload, string? exchange, CancellationToken cancellationToken)
        {
            events.Enqueue("send");
            if (blockSends)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            _published.TrySetResult(payload ?? "{}");
            return new MessageSendResult(true, "Published.");
        }

        public async Task<MessageListenResult> ListenAsync(Connection connection, string operationKey, TimeSpan timeout, string exchange, CancellationToken cancellationToken, Action? onListening = null)
        {
            await Task.Delay(50, cancellationToken);
            events.Enqueue("subscribed");
            onListening?.Invoke();
            if (blockListens)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            var payload = await _published.Task.WaitAsync(cancellationToken);
            return new MessageListenResult(true, "Received.", payload);
        }
    }
}
