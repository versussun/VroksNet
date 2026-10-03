using VroksNet.Application.Connections.CreateConnection;
using VroksNet.Application.TestScenarios.CreateTestScenario;
using VroksNet.Application.TestScenarios.DeleteTestScenario;
using VroksNet.Application.TestScenarios.GetTestScenario;
using VroksNet.Application.TestScenarios.ListTestScenarios;
using VroksNet.Application.TestScenarios.PreviewSchedule;
using VroksNet.Application.TestScenarios.UpdateTestScenario;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.Connections;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Domain.TestRuns;
using VroksNet.Infrastructure.Scheduling;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.TestScenarios;

public class TestScenarioHandlersTests
{
    [Fact]
    public async Task Create_HttpOperationAndHttpConnection_Succeeds()
    {
        var (specifications, connections, httpEndpointId, _, httpConnectionId, _) = await SeedAsync();
        var handler = new CreateTestScenarioHandler(new FakeTestScenarioRepository(), specifications, connections, new CronSchedule());

        var id = await handler.Handle(
            new CreateTestScenario("Send GET /pets", SpecId, httpEndpointId, httpConnectionId, null),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task Create_HttpOperationThroughRabbitMqConnection_Throws()
    {
        var (specifications, connections, httpEndpointId, _, _, rabbitConnectionId) = await SeedAsync();
        var handler = new CreateTestScenarioHandler(new FakeTestScenarioRepository(), specifications, connections, new CronSchedule());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new CreateTestScenario("Mismatched", SpecId, httpEndpointId, rabbitConnectionId, null), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Create_AsyncApiOperationAndRabbitMqConnection_Succeeds()
    {
        var (specifications, connections, _, asyncEndpointId, _, rabbitConnectionId) = await SeedAsync();
        var handler = new CreateTestScenarioHandler(new FakeTestScenarioRepository(), specifications, connections, new CronSchedule());

        var id = await handler.Handle(
            new CreateTestScenario("Publish order.created", SpecId, asyncEndpointId, rabbitConnectionId, null),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task Create_UnknownSpecification_Throws()
    {
        var (specifications, connections, _, _, httpConnectionId, _) = await SeedAsync();
        var handler = new CreateTestScenarioHandler(new FakeTestScenarioRepository(), specifications, connections, new CronSchedule());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new CreateTestScenario("x", Guid.NewGuid(), Guid.NewGuid(), httpConnectionId, null), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Create_BlankName_Throws()
    {
        var (specifications, connections, httpEndpointId, _, httpConnectionId, _) = await SeedAsync();
        var handler = new CreateTestScenarioHandler(new FakeTestScenarioRepository(), specifications, connections, new CronSchedule());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new CreateTestScenario("  ", SpecId, httpEndpointId, httpConnectionId, null), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Update_ExistingId_UpdatesFields()
    {
        var (specifications, connections, httpEndpointId, asyncEndpointId, httpConnectionId, rabbitConnectionId) = await SeedAsync();
        var repository = new FakeTestScenarioRepository();
        var id = await new CreateTestScenarioHandler(repository, specifications, connections, new CronSchedule()).Handle(
            new CreateTestScenario("Original", SpecId, httpEndpointId, httpConnectionId, null), TestContext.Current.CancellationToken);

        var updateHandler = new UpdateTestScenarioHandler(repository, specifications, connections, new FakeTestRunRepository(), new CronSchedule());
        var found = await updateHandler.Handle(
            new UpdateTestScenario(id, "Renamed", SpecId, asyncEndpointId, rabbitConnectionId, "{\"custom\":true}"),
            TestContext.Current.CancellationToken);

        Assert.True(found);
        var stored = await repository.FindByIdAsync(id, TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.Equal("Renamed", stored.Name);
        Assert.Equal(asyncEndpointId, stored.MockEndpointId);
        Assert.Equal(rabbitConnectionId, stored.ConnectionId);
        Assert.Equal("{\"custom\":true}", stored.PayloadOverride);
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsFalse()
    {
        var (specifications, connections, httpEndpointId, _, httpConnectionId, _) = await SeedAsync();
        var handler = new UpdateTestScenarioHandler(new FakeTestScenarioRepository(), specifications, connections, new FakeTestRunRepository(), new CronSchedule());

        var found = await handler.Handle(
            new UpdateTestScenario(Guid.NewGuid(), "x", SpecId, httpEndpointId, httpConnectionId, null), TestContext.Current.CancellationToken);

        Assert.False(found);
    }

    [Fact]
    public async Task Delete_ExistingId_RemovesIt()
    {
        var (specifications, connections, httpEndpointId, _, httpConnectionId, _) = await SeedAsync();
        var repository = new FakeTestScenarioRepository();
        var id = await new CreateTestScenarioHandler(repository, specifications, connections, new CronSchedule()).Handle(
            new CreateTestScenario("Original", SpecId, httpEndpointId, httpConnectionId, null), TestContext.Current.CancellationToken);

        var found = await new DeleteTestScenarioHandler(repository, new FakeTestRunRepository()).Handle(new DeleteTestScenario(id), TestContext.Current.CancellationToken);

        Assert.True(found);
        Assert.Empty(await repository.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task List_ReturnsDenormalizedSummaries()
    {
        var (specifications, connections, httpEndpointId, _, httpConnectionId, _) = await SeedAsync();
        var repository = new FakeTestScenarioRepository();
        await new CreateTestScenarioHandler(repository, specifications, connections, new CronSchedule()).Handle(
            new CreateTestScenario("Send GET /pets", SpecId, httpEndpointId, httpConnectionId, null), TestContext.Current.CancellationToken);

        var result = await new ListTestScenariosHandler(repository, specifications, connections, new CronSchedule(), TimeProvider.System)
            .Handle(new ListTestScenarios(), TestContext.Current.CancellationToken);

        var summary = Assert.Single(result);
        Assert.Equal("Send GET /pets", summary.Name);
        Assert.Equal("Petstore", summary.SpecificationTitle);
        Assert.Equal("GET /pets", summary.OperationKey);
        Assert.Equal("Orders API", summary.ConnectionName);
        Assert.Equal(ConnectionServiceType.Http, summary.ConnectionServiceType);
        Assert.Null(summary.LastRunAt);
        Assert.Null(summary.LastRunSuccess);
    }

    [Fact]
    public async Task Get_ExistingId_ReturnsSummaryWithLastRunStatus()
    {
        var (specifications, connections, httpEndpointId, _, httpConnectionId, _) = await SeedAsync();
        var repository = new FakeTestScenarioRepository();
        var id = await new CreateTestScenarioHandler(repository, specifications, connections, new CronSchedule()).Handle(
            new CreateTestScenario("Send GET /pets", SpecId, httpEndpointId, httpConnectionId, null), TestContext.Current.CancellationToken);

        var ranAt = DateTimeOffset.UtcNow;
        await repository.RecordRunAsync(id, ranAt, success: true, "200 OK", TestContext.Current.CancellationToken);

        var summary = await new GetTestScenarioHandler(repository, specifications, connections, new CronSchedule(), TimeProvider.System)
            .Handle(new GetTestScenario(id), TestContext.Current.CancellationToken);

        Assert.NotNull(summary);
        Assert.Equal("Send GET /pets", summary.Name);
        Assert.Equal(ranAt, summary.LastRunAt);
        Assert.True(summary.LastRunSuccess);
        Assert.Equal("200 OK", summary.LastRunMessage);
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsNull()
    {
        var (specifications, connections, _, _, _, _) = await SeedAsync();
        var handler = new GetTestScenarioHandler(new FakeTestScenarioRepository(), specifications, connections, new CronSchedule(), TimeProvider.System);

        var summary = await handler.Handle(new GetTestScenario(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Null(summary);
    }

    [Fact]
    public async Task List_DeletedConnection_ShowsPlaceholder()
    {
        var (specifications, connections, httpEndpointId, _, httpConnectionId, _) = await SeedAsync();
        var repository = new FakeTestScenarioRepository();
        await new CreateTestScenarioHandler(repository, specifications, connections, new CronSchedule()).Handle(
            new CreateTestScenario("Send GET /pets", SpecId, httpEndpointId, httpConnectionId, null), TestContext.Current.CancellationToken);

        await connections.DeleteAsync(httpConnectionId, TestContext.Current.CancellationToken);

        var result = await new ListTestScenariosHandler(repository, specifications, connections, new CronSchedule(), TimeProvider.System)
            .Handle(new ListTestScenarios(), TestContext.Current.CancellationToken);

        var summary = Assert.Single(result);
        Assert.Equal("(deleted connection)", summary.ConnectionName);
    }

    [Fact]
    public async Task Create_WithASchedule_StoresItNormalized_AndListShowsTheNextRun()
    {
        var (specifications, connections, httpEndpointId, _, httpConnectionId, _) = await SeedAsync();
        var repository = new FakeTestScenarioRepository();
        var id = await new CreateTestScenarioHandler(repository, specifications, connections, new CronSchedule()).Handle(
            new CreateTestScenario("Weekday mornings", SpecId, httpEndpointId, httpConnectionId, null, Schedule: "  0  9 * * 1-5 ", ScheduleTimeZone: " Europe/Kyiv "),
            TestContext.Current.CancellationToken);

        var stored = await repository.FindByIdAsync(id, TestContext.Current.CancellationToken);
        Assert.Equal("0 9 * * 1-5", stored!.Schedule);
        Assert.Equal("Europe/Kyiv", stored.ScheduleTimeZone);

        // Friday 3 July 2026, noon UTC → Monday 09:00 EEST.
        var summary = Assert.Single(await new ListTestScenariosHandler(repository, specifications, connections, new CronSchedule(), new FixedTimeProvider(DateTimeOffset.Parse("2026-07-03T12:00Z", System.Globalization.CultureInfo.InvariantCulture)))
            .Handle(new ListTestScenarios(), TestContext.Current.CancellationToken));
        Assert.Equal("0 9 * * 1-5", summary.Schedule);
        Assert.Equal(DateTimeOffset.Parse("2026-07-06T06:00Z", System.Globalization.CultureInfo.InvariantCulture), summary.NextScheduledRunAt);
    }

    [Theory]
    [InlineData("every minute", null, "isn't a valid cron expression")]
    [InlineData("0 9 * * *", "Mars/Olympus", "isn't a known time zone")]
    [InlineData(null, "Europe/Kyiv", "A time zone needs a schedule")]
    public async Task Create_WithABadSchedule_ThrowsWithTheReason(string? schedule, string? timeZone, string reason)
    {
        var (specifications, connections, httpEndpointId, _, httpConnectionId, _) = await SeedAsync();
        var handler = new CreateTestScenarioHandler(new FakeTestScenarioRepository(), specifications, connections, new CronSchedule());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(
            new CreateTestScenario("Bad schedule", SpecId, httpEndpointId, httpConnectionId, null, Schedule: schedule, ScheduleTimeZone: timeZone),
            TestContext.Current.CancellationToken).AsTask());
        Assert.Contains(reason, ex.Message);
    }

    [Fact]
    public async Task Update_ChangingTheSchedule_DropsTheRunQueuedOnTheOldOne_KeepingItOtherwise()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (specifications, connections, httpEndpointId, _, httpConnectionId, _) = await SeedAsync();
        var repository = new FakeTestScenarioRepository();
        var runs = new FakeTestRunRepository();
        var id = await new CreateTestScenarioHandler(repository, specifications, connections, new CronSchedule()).Handle(
            new CreateTestScenario("Hourly", SpecId, httpEndpointId, httpConnectionId, null, Schedule: "0 * * * *"), cancellationToken);
        await runs.InsertAsync(new TestRun { Id = Guid.NewGuid(), TestScenarioId = id, Status = TestRunStatus.Queued, Trigger = TestRunTrigger.Schedule, ScheduledFor = DateTimeOffset.UtcNow.AddMinutes(30) }, cancellationToken);
        var update = new UpdateTestScenarioHandler(repository, specifications, connections, runs, new CronSchedule());

        await update.Handle(new UpdateTestScenario(id, "Hourly, renamed", SpecId, httpEndpointId, httpConnectionId, null, Schedule: "0 * * * *"), cancellationToken);
        Assert.Single(runs.All); // same schedule: the queued run stands

        await update.Handle(new UpdateTestScenario(id, "Every minute", SpecId, httpEndpointId, httpConnectionId, null, Schedule: "*/1 * * * *"), cancellationToken);
        Assert.Empty(runs.All); // the worker plans the next one on the new schedule
    }

    [Fact]
    public async Task Delete_DropsTheQueuedScheduledRun_KeepingTheHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (specifications, connections, httpEndpointId, _, httpConnectionId, _) = await SeedAsync();
        var repository = new FakeTestScenarioRepository();
        var runs = new FakeTestRunRepository();
        var id = await new CreateTestScenarioHandler(repository, specifications, connections, new CronSchedule()).Handle(
            new CreateTestScenario("Hourly", SpecId, httpEndpointId, httpConnectionId, null, Schedule: "0 * * * *"), cancellationToken);
        await runs.InsertAsync(new TestRun { Id = Guid.NewGuid(), TestScenarioId = id, Status = TestRunStatus.Passed, Trigger = TestRunTrigger.Schedule, ScheduledFor = DateTimeOffset.UtcNow.AddMinutes(-30) }, cancellationToken);
        await runs.InsertAsync(new TestRun { Id = Guid.NewGuid(), TestScenarioId = id, Status = TestRunStatus.Queued, Trigger = TestRunTrigger.Schedule, ScheduledFor = DateTimeOffset.UtcNow.AddMinutes(30) }, cancellationToken);

        await new DeleteTestScenarioHandler(repository, runs).Handle(new DeleteTestScenario(id), cancellationToken);

        Assert.Equal(TestRunStatus.Passed, Assert.Single(runs.All).Status);
    }

    [Fact]
    public async Task PreviewSchedule_ListsTheNextRuns_OrSaysWhatsWrong()
    {
        var handler = new PreviewScheduleHandler(new CronSchedule(), new FixedTimeProvider(DateTimeOffset.Parse("2026-10-03T10:00:30Z", System.Globalization.CultureInfo.InvariantCulture)));

        var preview = await handler.Handle(new PreviewSchedule("*/15 * * * *", null, 3), TestContext.Current.CancellationToken);
        Assert.Null(preview.Error);
        Assert.Equal(["10:15", "10:30", "10:45"], preview.NextRuns.Select(run => run.UtcDateTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)));

        var invalid = await handler.Handle(new PreviewSchedule("*/15 * * *", null), TestContext.Current.CancellationToken);
        Assert.Contains("isn't a valid cron expression", invalid.Error);
        Assert.Empty(invalid.NextRuns);

        var none = await handler.Handle(new PreviewSchedule(" ", null), TestContext.Current.CancellationToken);
        Assert.Null(none.Error);
        Assert.Empty(none.NextRuns);
    }

    private static readonly Guid SpecId = Guid.NewGuid();

    private static async Task<(FakeApiSpecificationRepository Specifications, FakeConnectionRepository Connections, Guid HttpEndpointId, Guid AsyncEndpointId, Guid HttpConnectionId, Guid RabbitConnectionId)> SeedAsync()
    {
        var httpEndpointId = Guid.NewGuid();
        var asyncEndpointId = Guid.NewGuid();

        var specifications = new FakeApiSpecificationRepository();
        await specifications.UpsertAsync(new ApiSpecification
        {
            Id = SpecId,
            Title = "Petstore",
            Kind = SpecificationKind.OpenApi,
            Endpoints =
            [
                new MockEndpoint { Id = httpEndpointId, SpecificationId = SpecId, OperationKey = "GET /pets", ExampleTemplate = "[]" },
                new MockEndpoint { Id = asyncEndpointId, SpecificationId = SpecId, OperationKey = "orders.created:send", ExampleTemplate = "{}" }
            ]
        }, TestContext.Current.CancellationToken);

        var connections = new FakeConnectionRepository();
        var httpConnectionId = Guid.NewGuid();
        var rabbitConnectionId = Guid.NewGuid();
        await connections.InsertAsync(new Connection { Id = httpConnectionId, Name = "Orders API", ServiceType = ConnectionServiceType.Http, Value = "https://api.example.com" }, TestContext.Current.CancellationToken);
        await connections.InsertAsync(new Connection { Id = rabbitConnectionId, Name = "Orders Broker", ServiceType = ConnectionServiceType.RabbitMq, Value = "amqp://localhost" }, TestContext.Current.CancellationToken);

        return (specifications, connections, httpEndpointId, asyncEndpointId, httpConnectionId, rabbitConnectionId);
    }
}
