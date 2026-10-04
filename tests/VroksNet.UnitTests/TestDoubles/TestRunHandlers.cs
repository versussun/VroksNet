using VroksNet.Application.Abstractions;
using VroksNet.Application.TestRuns;
using VroksNet.Application.TestScenarios;
using VroksNet.Application.TestScenarios.RunTestScenario;
using VroksNet.Infrastructure.Templating;

namespace VroksNet.UnitTests.TestDoubles;

/// <summary>Builds a <see cref="RunTestScenarioHandler"/> from the dependencies tests already arrange, wiring the shared <see cref="TestScenarioExecutor"/> and run bookkeeping around them.</summary>
internal static class TestRunHandlers
{
    public static RunTestScenarioHandler Run(
        ITestScenarioRepository scenarios,
        IApiSpecificationRepository specifications,
        IConnectionRepository connections,
        IMessageSender sender,
        IMessageListener listener,
        ISchemaValidator schemaValidator,
        ICallRecordRepository callRecords,
        FakeTestRunRepository? runs = null,
        TestRunCancellations? cancellations = null)
        => new(
            scenarios,
            runs ?? new FakeTestRunRepository(),
            Executor(scenarios, specifications, connections, sender, listener, schemaValidator, callRecords),
            cancellations ?? new TestRunCancellations(),
            TimeProvider.System);

    public static TestScenarioExecutor Executor(
        ITestScenarioRepository scenarios,
        IApiSpecificationRepository specifications,
        IConnectionRepository connections,
        IMessageSender sender,
        IMessageListener listener,
        ISchemaValidator schemaValidator,
        ICallRecordRepository callRecords)
        => new(scenarios, specifications, connections, sender, listener, schemaValidator, new ResponseTemplateEngine(TimeProvider.System), callRecords);
}
