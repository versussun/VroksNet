using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestScenarios.DeleteTestScenario;

public sealed class DeleteTestScenarioHandler(ITestScenarioRepository repository) : IRequestHandler<DeleteTestScenario, bool>
{
    public ValueTask<bool> Handle(DeleteTestScenario request, CancellationToken cancellationToken)
        => new(repository.DeleteAsync(request.Id, cancellationToken));
}
