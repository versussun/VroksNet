using Microsoft.Extensions.Logging.Abstractions;
using VroksNet.Application.Abstractions;
using VroksNet.Application.Specifications.ImportAsyncApiSpec;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.Specifications;

public class ImportAsyncApiSpecHandlerTests
{
    [Fact]
    public async Task Handle_NewTitle_InsertsSpecificationWithParsedEndpointsAndAsyncApiKind()
    {
        var repository = new FakeApiSpecificationRepository();
        var parser = new FakeAsyncApiSpecificationParser(new ParsedSpecification(
            "Orders Events Sample API",
            [new ParsedOperation("orders.created:send", "{}"), new ParsedOperation("orders.shipped:receive", null)]));
        var handler = new ImportAsyncApiSpecHandler(parser, repository, NullLogger<ImportAsyncApiSpecHandler>.Instance);

        var id = await handler.Handle(new ImportAsyncApiSpec("orders.yaml", "raw yaml"), TestContext.Current.CancellationToken);

        var stored = await repository.FindByTitleAsync("Orders Events Sample API", TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(id, stored.Id);
        Assert.Equal(SpecificationKind.AsyncApi, stored.Kind);
        Assert.Equal(2, stored.Endpoints.Count);
        Assert.Contains(stored.Endpoints, e => e.OperationKey == "orders.created:send" && e.ExampleTemplate == "{}");
    }

    [Fact]
    public async Task Handle_ExistingTitle_ReplacesButKeepsSameId()
    {
        var repository = new FakeApiSpecificationRepository();
        var handler = new ImportAsyncApiSpecHandler(
            new FakeAsyncApiSpecificationParser(new ParsedSpecification("Orders Events Sample API", [new ParsedOperation("orders.created:send", null)])),
            repository,
            NullLogger<ImportAsyncApiSpecHandler>.Instance);

        var firstId = await handler.Handle(new ImportAsyncApiSpec("orders-v1.yaml", "raw yaml v1"), TestContext.Current.CancellationToken);

        var handlerV2 = new ImportAsyncApiSpecHandler(
            new FakeAsyncApiSpecificationParser(new ParsedSpecification(
                "Orders Events Sample API",
                [new ParsedOperation("orders.created:send", null), new ParsedOperation("orders.shipped:receive", null)])),
            repository,
            NullLogger<ImportAsyncApiSpecHandler>.Instance);

        var secondId = await handlerV2.Handle(new ImportAsyncApiSpec("orders-v2.yaml", "raw yaml v2"), TestContext.Current.CancellationToken);

        Assert.Equal(firstId, secondId);

        var all = await repository.ListAsync(TestContext.Current.CancellationToken);
        var stored = Assert.Single(all);
        Assert.Equal(2, stored.Endpoints.Count);
        Assert.Equal("raw yaml v2", stored.RawContent);
    }
}
