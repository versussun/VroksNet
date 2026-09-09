using VroksNet.Application.Abstractions;

namespace VroksNet.UnitTests.TestDoubles;

internal sealed class FakeAsyncApiSpecificationParser(ParsedSpecification result) : IAsyncApiSpecificationParser
{
    public Task<ParsedSpecification> ParseAsync(string rawContent, CancellationToken cancellationToken)
        => Task.FromResult(result);
}
