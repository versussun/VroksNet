using Microsoft.Extensions.Logging.Abstractions;
using VroksNet.Application.Abstractions;
using VroksNet.Application.Mocking.InvokeMockEndpoint;
using VroksNet.Application.Mocking.SetEndpointEnabled;
using VroksNet.Application.Specifications.ImportOpenApiSpec;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Infrastructure.SchemaValidation;
using VroksNet.Infrastructure.Templating;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.Mocking;

public sealed class SetEndpointEnabledHandlerTests
{
    private readonly FakeApiSpecificationRepository _specifications = new();

    [Fact]
    public async Task Disable_StopsTheMockAnswering_AndEnableBringsItBack()
    {
        var (_, endpoint) = await AddSpecAsync("Petstore", "GET /pets");

        var disabled = await Handler.Handle(new SetEndpointEnabled(endpoint.Id, false), TestContext.Current.CancellationToken);

        Assert.True(disabled.Succeeded);
        Assert.False(endpoint.IsEnabled);
        Assert.False((await InvokeAsync("GET", "/pets")).Matched);

        var enabled = await Handler.Handle(new SetEndpointEnabled(endpoint.Id, true), TestContext.Current.CancellationToken);

        Assert.True(enabled.Succeeded);
        Assert.True((await InvokeAsync("GET", "/pets")).Matched);
    }

    [Fact]
    public async Task Disable_AlsoStopsTheProviderPortAnswering()
    {
        var (_, endpoint) = await AddSpecAsync("Petstore", "GET /pets");
        endpoint.ServeAtRealPath = true;

        await Handler.Handle(new SetEndpointEnabled(endpoint.Id, false), TestContext.Current.CancellationToken);

        Assert.False((await InvokeAsync("GET", "/pets", providerMode: true)).Matched);
        Assert.True(endpoint.ServeAtRealPath); // the provider switch itself is left alone
    }

    [Fact]
    public async Task UnknownEndpoint_IsNotFound()
    {
        var result = await Handler.Handle(new SetEndpointEnabled(Guid.NewGuid(), false), TestContext.Current.CancellationToken);

        Assert.False(result.Found);
    }

    [Fact]
    public async Task AsyncApiOperation_IsRefused()
    {
        var (_, endpoint) = await AddSpecAsync("Orders", "orders.created:send");

        var result = await Handler.Handle(new SetEndpointEnabled(endpoint.Id, false), TestContext.Current.CancellationToken);

        Assert.True(result.Found);
        Assert.Contains("Only HTTP operations", result.Refusal);
        Assert.True(endpoint.IsEnabled);
    }

    [Fact]
    public async Task WhenAConcurrentReimportReplacedTheEndpoint_IsRefusedInsteadOfReportingSuccess()
    {
        var (_, endpoint) = await AddSpecAsync("Petstore", "GET /pets");
        _specifications.SimulateConcurrentReimport = true;

        var result = await Handler.Handle(new SetEndpointEnabled(endpoint.Id, false), TestContext.Current.CancellationToken);

        Assert.Contains("changed while", result.Refusal);
    }

    [Fact]
    public async Task Reimport_KeepsOperationsThatWereTurnedOffOff()
    {
        var (_, disabled) = await AddSpecAsync("Petstore", "GET /pets", "GET /pets/{id}");
        disabled.IsEnabled = false;

        var parser = new FakeSpecificationParser(new ParsedSpecification("Petstore", [new ParsedOperation("GET /pets", null), new ParsedOperation("GET /pets/{id}", null), new ParsedOperation("POST /pets", null)]));
        await new ImportOpenApiSpecHandler(parser, _specifications, NullLogger<ImportOpenApiSpecHandler>.Instance)
            .Handle(new ImportOpenApiSpec("petstore.yaml", "openapi: 3.0.3"), TestContext.Current.CancellationToken);

        var reimported = await _specifications.FindByTitleAsync("Petstore", TestContext.Current.CancellationToken);
        Assert.False(reimported!.Endpoints.Single(e => e.OperationKey == "GET /pets").IsEnabled);
        Assert.True(reimported.Endpoints.Single(e => e.OperationKey == "GET /pets/{id}").IsEnabled);
        Assert.True(reimported.Endpoints.Single(e => e.OperationKey == "POST /pets").IsEnabled);
    }

    private SetEndpointEnabledHandler Handler => new(_specifications);

    private ValueTask<MockInvocationResult> InvokeAsync(string method, string path, bool providerMode = false)
        => new InvokeMockEndpointHandler(_specifications, new FakeCallRecordRepository(), new SchemaValidator(), new ResponseTemplateEngine(TimeProvider.System), NullLogger<InvokeMockEndpointHandler>.Instance)
            .Handle(new InvokeMockEndpoint(method, path, ProviderMode: providerMode), TestContext.Current.CancellationToken);

    /// <summary>Adds a spec with the given operations; returns it and its first operation.</summary>
    private async Task<(ApiSpecification Specification, MockEndpoint First)> AddSpecAsync(string title, params string[] operationKeys)
    {
        var specification = new ApiSpecification { Id = Guid.NewGuid(), Title = title, Kind = SpecificationKind.OpenApi, RawContent = "raw" };
        specification.Endpoints = operationKeys
            .Select(key => new MockEndpoint { Id = Guid.NewGuid(), SpecificationId = specification.Id, OperationKey = key })
            .ToList();
        await _specifications.UpsertAsync(specification, TestContext.Current.CancellationToken);
        return (specification, specification.Endpoints.First());
    }
}
