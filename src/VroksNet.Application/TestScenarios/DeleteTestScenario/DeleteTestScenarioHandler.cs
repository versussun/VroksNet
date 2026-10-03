using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestScenarios.DeleteTestScenario;

/// <summary>The scenario's history stays (ADR 0002); only its queued scheduled run goes, since nothing would run it.</summary>
public sealed class DeleteTestScenarioHandler(ITestScenarioRepository repository, ITestRunRepository runs) : IRequestHandler<DeleteTestScenario, bool>
{
    public async ValueTask<bool> Handle(DeleteTestScenario request, CancellationToken cancellationToken)
    {
        if (!await repository.DeleteAsync(request.Id, cancellationToken))
        {
            return false;
        }

        await runs.DeleteQueuedScheduledAsync(request.Id, cancellationToken);
        return true;
    }
}
