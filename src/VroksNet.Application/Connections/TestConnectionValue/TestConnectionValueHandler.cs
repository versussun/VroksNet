using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;

namespace VroksNet.Application.Connections.TestConnectionValue;

public sealed class TestConnectionValueHandler(IConnectionTester tester) : IRequestHandler<TestConnectionValue, ConnectionTestResult>
{
    public async ValueTask<ConnectionTestResult> Handle(TestConnectionValue request, CancellationToken cancellationToken)
    {
        // A throwaway Connection — Id/Name/timestamps are irrelevant here, IConnectionTester only
        // ever reads ServiceType/Value (see ConnectionTester).
        var connection = new Connection { ServiceType = request.ServiceType, Value = request.Value };
        return await tester.TestAsync(connection, cancellationToken);
    }
}
