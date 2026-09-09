using VroksNet.Application.Abstractions;
using VroksNet.Domain.Connections;

namespace VroksNet.UnitTests.TestDoubles;

internal sealed class FakeConnectionTester(ConnectionTestResult result) : IConnectionTester
{
    public Connection? LastTested { get; private set; }

    public Task<ConnectionTestResult> TestAsync(Connection connection, CancellationToken cancellationToken)
    {
        LastTested = connection;
        return Task.FromResult(result);
    }
}
