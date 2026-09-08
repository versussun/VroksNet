using Mediator;
using VroksNet.Application.Abstractions;

namespace VroksNet.Application.Mocking.InvokeMockEndpoint;

public sealed class InvokeMockEndpointHandler(IApiSpecificationRepository repository)
    : IRequestHandler<InvokeMockEndpoint, MockInvocationResult>
{
    public async ValueTask<MockInvocationResult> Handle(InvokeMockEndpoint request, CancellationToken cancellationToken)
    {
        var specifications = await repository.ListAsync(cancellationToken);

        var match = specifications
            .SelectMany(specification => specification.Endpoints)
            .Where(endpoint => endpoint.IsEnabled)
            .FirstOrDefault(endpoint => OperationKeyMatcher.Matches(endpoint.OperationKey, request.Method, request.Path));

        return match is null
            ? new MockInvocationResult(false, null, null)
            : new MockInvocationResult(true, match.OperationKey, match.ExampleTemplate);
    }
}
