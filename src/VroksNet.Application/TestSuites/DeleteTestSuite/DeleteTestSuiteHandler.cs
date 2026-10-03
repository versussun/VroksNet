using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.TestSuites.DeleteTestSuite;

public sealed class DeleteTestSuiteHandler(ITestSuiteRepository suites) : IRequestHandler<DeleteTestSuite, bool>
{
    public async ValueTask<bool> Handle(DeleteTestSuite request, CancellationToken cancellationToken)
        => await suites.DeleteAsync(request.Id, cancellationToken);
}
