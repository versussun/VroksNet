using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.TestSuites;

namespace VroksNet.Application.TestSuites.CreateTestSuite;

public sealed class CreateTestSuiteHandler(
    ITestSuiteRepository suites,
    ITestScenarioRepository scenarios,
    TimeProvider timeProvider) : IRequestHandler<CreateTestSuite, Guid>
{
    public async ValueTask<Guid> Handle(CreateTestSuite request, CancellationToken cancellationToken)
    {
        var name = UniqueNames.Normalize(request.Name, "test suite");
        UniqueNames.EnsureFree((await suites.FindByNameAsync(name, cancellationToken))?.Id, null, "test suite", name);
        var scenarioIds = await TestSuiteScenarios.ValidateAsync(scenarios, request.TestScenarioIds, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var suite = new TestSuite { Id = Guid.NewGuid(), Name = name, TestScenarioIds = scenarioIds, CreatedAt = now, UpdatedAt = now };
        await suites.InsertAsync(suite, cancellationToken);
        return suite.Id;
    }
}
