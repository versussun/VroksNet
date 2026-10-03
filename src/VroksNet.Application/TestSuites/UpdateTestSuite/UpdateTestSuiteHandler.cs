using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestSuites.UpdateTestSuite;

public sealed class UpdateTestSuiteHandler(
    ITestSuiteRepository suites,
    ITestScenarioRepository scenarios,
    TimeProvider timeProvider) : IRequestHandler<UpdateTestSuite, bool>
{
    public async ValueTask<bool> Handle(UpdateTestSuite request, CancellationToken cancellationToken)
    {
        var name = UniqueNames.Normalize(request.Name, "test suite");
        var suite = await suites.FindByIdAsync(request.Id, cancellationToken);
        if (suite is null)
        {
            return false;
        }

        UniqueNames.EnsureFree((await suites.FindByNameAsync(name, cancellationToken))?.Id, suite.Id, "test suite", name);
        suite.TestScenarioIds = await TestSuiteScenarios.ValidateAsync(scenarios, request.TestScenarioIds, cancellationToken);
        suite.Name = name;
        suite.UpdatedAt = timeProvider.GetUtcNow();
        return await suites.UpdateAsync(suite, cancellationToken);
    }
}
