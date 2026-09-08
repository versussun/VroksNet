using VroksNet.Application.Abstractions;

namespace VroksNet.UnitTests.TestDoubles;

internal sealed class FakeSpecificationParser(ParsedSpecification result) : ISpecificationParser
{
    public Task<ParsedSpecification> ParseAsync(string rawContent, CancellationToken cancellationToken)
        => Task.FromResult(result);
}
