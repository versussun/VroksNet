using Microsoft.Extensions.Logging.Abstractions;
using VroksNet.Application.Abstractions;
using VroksNet.Application.Specifications.ImportProtoSpecs;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Infrastructure.Specifications.Proto;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.Specifications;

/// <summary><see cref="ImportProtoSpecsHandler"/> with the real parser (pure and in-process) over the in-memory repository.</summary>
public sealed class ImportProtoSpecsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);
    private readonly FakeApiSpecificationRepository _repository = new();

    [Fact]
    public async Task Handle_StoresOneProtoSpecificationPerFileWithServices()
    {
        var result = await ImportAsync(ProtoSpecificationParserTests.Samples());

        Assert.Equal(["common/money.proto"], result.ImportOnlyFiles);
        Assert.All(result.Specifications, imported => Assert.False(imported.Replaced));
        var shop = await _repository.FindByTitleAsync("shop.orders.v1 (Orders, Payments)", TestContext.Current.CancellationToken);
        Assert.NotNull(shop);
        Assert.Equal((SpecificationKind.Proto, "shop-orders.proto", Now), (shop.Kind, shop.ProtoPath, shop.CreatedAt));
        Assert.Contains("service Orders", shop.RawContent);
        Assert.Equal(4, shop.Endpoints.Count);
        Assert.All(shop.Endpoints, endpoint => Assert.NotNull(endpoint.RequestSchema));
        Assert.Equal(4, result.Specifications.Single(imported => imported.Id == shop.Id).OperationCount);
    }

    [Fact]
    public async Task Handle_TheSameFilesAgain_ReplaceInPlace_KeepingOperationIds()
    {
        await ImportAsync(ProtoSpecificationParserTests.Samples());
        var before = await _repository.FindByTitleAsync("demo.greeter.v1 (Greeter)", TestContext.Current.CancellationToken);
        var endpointId = Assert.Single(before!.Endpoints).Id;

        var result = await ImportAsync(ProtoSpecificationParserTests.Samples());

        Assert.All(result.Specifications, imported => Assert.True(imported.Replaced));
        Assert.Equal(2, (await _repository.ListAsync(TestContext.Current.CancellationToken)).Count);
        var after = await _repository.FindByTitleAsync("demo.greeter.v1 (Greeter)", TestContext.Current.CancellationToken);
        Assert.Equal((before.Id, endpointId), (after!.Id, Assert.Single(after.Endpoints).Id));
    }

    [Fact]
    public async Task Handle_AServiceAdded_MakesANewSpecification_AndNotesTheOldOne()
    {
        await ImportAsync(new Dictionary<string, string> { ["greeter.proto"] = Greeter(services: "service Greeter { rpc Hi (M) returns (M); }") });

        var result = await ImportAsync(new Dictionary<string, string>
        {
            ["greeter.proto"] = Greeter(services: "service Greeter { rpc Hi (M) returns (M); } service Admin { rpc Reset (M) returns (M); }")
        });

        var imported = Assert.Single(result.Specifications);
        Assert.Equal("demo.v1 (Admin, Greeter)", imported.Title);
        Assert.False(imported.Replaced);
        Assert.Contains("\"demo.v1 (Greeter)\" comes from the same package but has other services, so it was kept as a separate specification.", imported.Note);
        // The old specification still serves Greeter/Hi, so the new one's copy comes in turned off (ADR 0004).
        Assert.Contains("/demo.v1.Greeter/Hi is already served by \"demo.v1 (Greeter)\", so it was imported turned off.", imported.Note);
        Assert.Equal(2, (await _repository.ListAsync(TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task Handle_FilesThatDontParse_Throw_AndStoreNothing()
    {
        await Assert.ThrowsAsync<SpecificationParseException>(async () => await ImportAsync(new Dictionary<string, string> { ["bad.proto"] = "syntax = \"proto3\"; message {" }));

        Assert.Empty(await _repository.ListAsync(TestContext.Current.CancellationToken));
    }

    private static string Greeter(string services) => $$"""
        syntax = "proto3";
        package demo.v1;
        message M { string text = 1; }
        {{services}}
        """;

    private async Task<ImportProtoResult> ImportAsync(Dictionary<string, string> files)
    {
        var handler = new ImportProtoSpecsHandler(new ProtoSpecificationParser(), _repository, new FixedTimeProvider(Now), NullLogger<ImportProtoSpecsHandler>.Instance);
        return await handler.Handle(new ImportProtoSpecs(files), TestContext.Current.CancellationToken);
    }
}
