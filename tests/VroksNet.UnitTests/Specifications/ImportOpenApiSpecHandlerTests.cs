using Microsoft.Extensions.Logging.Abstractions;
using VroksNet.Application.Abstractions;
using VroksNet.Application.Specifications.ImportOpenApiSpec;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.Specifications;

public class ImportOpenApiSpecHandlerTests
{
    [Fact]
    public async Task Handle_NewTitle_InsertsSpecificationWithParsedEndpoints()
    {
        var repository = new FakeApiSpecificationRepository();
        var parser = new FakeSpecificationParser(new ParsedSpecification(
            "Petstore Sample API",
            [new ParsedOperation("GET /pets", "[]"), new ParsedOperation("POST /pets", null)]));
        var handler = new ImportOpenApiSpecHandler(parser, repository, NullLogger<ImportOpenApiSpecHandler>.Instance);

        var id = await handler.Handle(new ImportOpenApiSpec("petstore.yaml", "raw yaml"), TestContext.Current.CancellationToken);

        var stored = await repository.FindByTitleAsync("Petstore Sample API", TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(id, stored.Id);
        Assert.Equal(2, stored.Endpoints.Count);
        Assert.Contains(stored.Endpoints, e => e.OperationKey == "GET /pets" && e.ExampleTemplate == "[]");
    }

    [Fact]
    public async Task Handle_ExistingTitle_ReplacesButKeepsSameId()
    {
        var repository = new FakeApiSpecificationRepository();
        var handler = new ImportOpenApiSpecHandler(
            new FakeSpecificationParser(new ParsedSpecification("Petstore Sample API", [new ParsedOperation("GET /pets", null)])),
            repository,
            NullLogger<ImportOpenApiSpecHandler>.Instance);

        var firstId = await handler.Handle(new ImportOpenApiSpec("petstore-v1.yaml", "raw yaml v1"), TestContext.Current.CancellationToken);

        var handlerV2 = new ImportOpenApiSpecHandler(
            new FakeSpecificationParser(new ParsedSpecification(
                "Petstore Sample API",
                [new ParsedOperation("GET /pets", null), new ParsedOperation("POST /pets", null), new ParsedOperation("DELETE /pets/{id}", null)])),
            repository,
            NullLogger<ImportOpenApiSpecHandler>.Instance);

        var secondId = await handlerV2.Handle(new ImportOpenApiSpec("petstore-v2.yaml", "raw yaml v2"), TestContext.Current.CancellationToken);

        Assert.Equal(firstId, secondId);

        var all = await repository.ListAsync(TestContext.Current.CancellationToken);
        var stored = Assert.Single(all);
        Assert.Equal(3, stored.Endpoints.Count);
        Assert.Equal("raw yaml v2", stored.RawContent);
    }
}
