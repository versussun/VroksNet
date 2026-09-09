using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.Connections.TestConnection;

public sealed class TestConnectionHandler(IConnectionRepository repository, IConnectionTester tester)
    : IRequestHandler<TestConnection, ConnectionTestResult?>
{
    public async ValueTask<ConnectionTestResult?> Handle(TestConnection request, CancellationToken cancellationToken)
    {
        var connection = await repository.FindByIdAsync(request.Id, cancellationToken);
        return connection is null ? null : await tester.TestAsync(connection, cancellationToken);
    }
}
