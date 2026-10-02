using Microsoft.Extensions.Logging.Abstractions;
using VroksNet.Application.Abstractions;
using VroksNet.Application.CallRecords;
using VroksNet.Application.Mocking.InvokeMockEndpoint;
using VroksNet.Application.Mocking.SetEndpointProviderMode;
using VroksNet.Application.Mocking.SetSpecificationProviderMode;
using VroksNet.Application.Specifications.ImportOpenApiSpec;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Infrastructure.SchemaValidation;
using VroksNet.Infrastructure.Templating;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.Mocking;

/// <summary>Provider mode ("Тип 3"): the overlap rule, turning it on/off, what the provider surface answers, and request validation.</summary>
public sealed class ProviderModeTests
{
    private const string NewPetSchema = """{ "type": "object", "required": ["name"], "properties": { "name": { "type": "string" } } }""";

    private readonly FakeApiSpecificationRepository _specifications = new();
    private readonly FakeCallRecordRepository _callRecords = new();

    [Theory]
    [InlineData("GET /pets/{id}", "GET /pets/mine", true)]     // /pets/mine matches both
    [InlineData("GET /pets/{id}", "GET /pets/{petId}", true)]  // same shape, different names
    [InlineData("GET /health", "get /health", true)]           // method case doesn't matter
    [InlineData("GET /pets/{id}", "POST /pets/{id}", false)]   // different method
    [InlineData("GET /pets/{id}", "GET /pets/{id}/toys", false)] // different length
    [InlineData("GET /pets/mine", "GET /pets/all", false)]     // different literal segment
    [InlineData("orders.created:send", "orders.created:send", false)] // not HTTP-shaped
    public void Overlaps(string a, string b, bool expected)
    {
        Assert.Equal(expected, OperationOverlap.Overlaps(a, b));
        Assert.Equal(expected, OperationOverlap.Overlaps(b, a));
    }

    [Fact]
    public async Task EnableEndpoint_NoOverlap_Succeeds()
    {
        var (_, endpoint) = await AddSpecAsync("Petstore", "GET /pets/{id}");

        var result = await EndpointHandler.Handle(new SetEndpointProviderMode(endpoint.Id, true), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.True(endpoint.ServeAtRealPath);
    }

    [Fact]
    public async Task EnableEndpoint_OverlappingAServedOperationInAnotherSpec_IsRefusedNamingIt()
    {
        var (_, served) = await AddSpecAsync("Petstore", "GET /pets/{id}");
        served.ServeAtRealPath = true;
        var (_, candidate) = await AddSpecAsync("Pets v2", "GET /pets/mine");

        var result = await EndpointHandler.Handle(new SetEndpointProviderMode(candidate.Id, true), TestContext.Current.CancellationToken);

        Assert.True(result.Found);
        Assert.False(result.Succeeded);
        Assert.Contains("GET /pets/{id} in \"Petstore\"", result.Refusal);
        Assert.False(candidate.ServeAtRealPath);
    }

    [Fact]
    public async Task EnableEndpoint_BrokerOperation_IsRefused()
    {
        var (_, endpoint) = await AddSpecAsync("Orders", "orders.created:send");

        var result = await EndpointHandler.Handle(new SetEndpointProviderMode(endpoint.Id, true), TestContext.Current.CancellationToken);

        Assert.Contains("Only HTTP operations", result.Refusal);
    }

    [Fact]
    public async Task DisableEndpoint_AlwaysSucceeds_AndUnknownIdIsNotFound()
    {
        var (_, endpoint) = await AddSpecAsync("Petstore", "GET /pets");
        endpoint.ServeAtRealPath = true;

        var disabled = await EndpointHandler.Handle(new SetEndpointProviderMode(endpoint.Id, false), TestContext.Current.CancellationToken);
        var unknown = await EndpointHandler.Handle(new SetEndpointProviderMode(Guid.NewGuid(), true), TestContext.Current.CancellationToken);

        Assert.True(disabled.Succeeded);
        Assert.False(endpoint.ServeAtRealPath);
        Assert.False(unknown.Found);
    }

    [Fact]
    public async Task EnableSpecification_ServesWhatItCan_AndSkipsOverlapsWithinAndAcrossSpecs()
    {
        var (_, other) = await AddSpecAsync("Health", "GET /health");
        other.ServeAtRealPath = true;
        var (specification, _) = await AddSpecAsync("Petstore", "GET /health", "GET /pets/mine", "GET /pets/{id}", "orders.created:send");

        var result = await SpecificationHandler.Handle(new SetSpecificationProviderMode(specification.Id, true), TestContext.Current.CancellationToken);

        // Key order: "GET /health" (overlaps the other spec), "GET /pets/mine" (served), then
        // "GET /pets/{id}" (overlaps /pets/mine, served a moment ago). The broker operation isn't HTTP.
        Assert.Equal(["GET /pets/mine"], result!.Served);
        Assert.Equal(["GET /health", "GET /pets/{id}"], result.Skipped.Select(s => s.OperationKey));
        Assert.Equal(["GET /pets/mine"], specification.Endpoints.Where(e => e.ServeAtRealPath).Select(e => e.OperationKey));
    }

    [Fact]
    public async Task DisableSpecification_TurnsAllOff()
    {
        var (specification, _) = await AddSpecAsync("Petstore", "GET /pets", "POST /pets");
        foreach (var endpoint in specification.Endpoints)
        {
            endpoint.ServeAtRealPath = true;
        }

        await SpecificationHandler.Handle(new SetSpecificationProviderMode(specification.Id, false), TestContext.Current.CancellationToken);

        Assert.All(specification.Endpoints, endpoint => Assert.False(endpoint.ServeAtRealPath));
    }

    [Fact]
    public async Task Invoke_ProviderMode_AnswersOnlyServedOperations_AtTheirRealPath()
    {
        var (_, served) = await AddSpecAsync("Petstore", "GET /pets");
        served.ServeAtRealPath = true;
        served.ExampleTemplate = "[]";
        await AddSpecAsync("Orders", "GET /orders"); // enabled, but not served at its real path

        var pets = await MockHandler.Handle(new InvokeMockEndpoint("GET", "/pets", ProviderMode: true), TestContext.Current.CancellationToken);
        var orders = await MockHandler.Handle(new InvokeMockEndpoint("GET", "/orders", ProviderMode: true), TestContext.Current.CancellationToken);
        var ordersViaPrefix = await MockHandler.Handle(new InvokeMockEndpoint("GET", "/orders"), TestContext.Current.CancellationToken);

        Assert.True(pets.Matched);
        Assert.False(orders.Matched);
        Assert.Contains("Serve at real path", orders.ResponseBody);
        Assert.True(ordersViaPrefix.Matched); // "/mock" still answers every enabled operation
        Assert.Equal(["GET /pets", "GET /orders", "GET /mock/orders"], _callRecords.Inserted.Select(r => r.RequestLine));
    }

    [Theory]
    [InlineData("""{"name":"Fido"}""", true)]
    [InlineData("""{"tag":"dog"}""", false)]
    [InlineData("not json", false)]
    public async Task Invoke_RequestBodyIsValidatedAgainstTheRequestSchema_ButStillAnswered(string body, bool expectValid)
    {
        var (_, endpoint) = await AddSpecAsync("Petstore", "POST /pets");
        endpoint.RequestSchema = NewPetSchema;

        var result = await MockHandler.Handle(new InvokeMockEndpoint("POST", "/pets", Body: body, ContentType: "application/json; charset=utf-8"), TestContext.Current.CancellationToken);

        Assert.True(result.Matched);
        var logged = Assert.Single(_callRecords.Inserted);
        Assert.Equal(expectValid, logged.ContractValid);
        Assert.Equal(expectValid, logged.ValidationErrors is null);
    }

    [Fact]
    public async Task Invoke_NoBodyOrOversizedBody_IsNotValidated()
    {
        var (_, endpoint) = await AddSpecAsync("Petstore", "POST /pets");
        endpoint.RequestSchema = NewPetSchema;

        await MockHandler.Handle(new InvokeMockEndpoint("POST", "/pets", ContentType: "application/json"), TestContext.Current.CancellationToken);
        await MockHandler.Handle(new InvokeMockEndpoint("POST", "/pets", Body: new string('x', CallRecordSnapshot.MaxLength + 1), ContentType: "application/json"), TestContext.Current.CancellationToken);

        Assert.All(_callRecords.Inserted, record => Assert.Null(record.ContractValid));
    }

    [Theory]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("text/plain")]
    [InlineData(null)]
    public async Task Invoke_NonJsonContentType_IsNotValidated(string? contentType)
    {
        var (_, endpoint) = await AddSpecAsync("Petstore", "POST /pets");
        endpoint.RequestSchema = NewPetSchema;

        await MockHandler.Handle(new InvokeMockEndpoint("POST", "/pets", Body: "name=Fido", ContentType: contentType), TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(_callRecords.Inserted).ContractValid);
    }

    [Fact]
    public async Task Invoke_PlusJsonContentType_IsValidated()
    {
        var (_, endpoint) = await AddSpecAsync("Petstore", "POST /pets");
        endpoint.RequestSchema = NewPetSchema;

        await MockHandler.Handle(new InvokeMockEndpoint("POST", "/pets", Body: """{"tag":"dog"}""", ContentType: "application/merge-patch+json"), TestContext.Current.CancellationToken);

        Assert.False(Assert.Single(_callRecords.Inserted).ContractValid);
    }

    [Fact]
    public async Task Invoke_Head_IsAnsweredLikeGet()
    {
        var (_, endpoint) = await AddSpecAsync("Petstore", "GET /pets");
        endpoint.ServeAtRealPath = true;

        var result = await MockHandler.Handle(new InvokeMockEndpoint("HEAD", "/pets", ProviderMode: true), TestContext.Current.CancellationToken);

        Assert.True(result.Matched);
    }

    [Fact]
    public async Task Enable_WhenAConcurrentReimportReplacedTheEndpoints_IsRefusedInsteadOfReportingSuccess()
    {
        var (specification, endpoint) = await AddSpecAsync("Petstore", "GET /pets");
        _specifications.SimulateConcurrentReimport = true;

        var single = await EndpointHandler.Handle(new SetEndpointProviderMode(endpoint.Id, true), TestContext.Current.CancellationToken);
        var all = await SpecificationHandler.Handle(new SetSpecificationProviderMode(specification.Id, true), TestContext.Current.CancellationToken);

        Assert.Contains("changed while", single.Refusal);
        Assert.Contains("changed while", all!.Refusal);
    }

    [Fact]
    public async Task EnableSpecification_AlreadyServedOperationsCountAsServed_NotSkipped()
    {
        // Two served operations that overlap each other (possible only via a past race) must not
        // be reported as skipped — they're served.
        var (specification, _) = await AddSpecAsync("Petstore", "GET /pets/mine", "GET /pets/{id}");
        foreach (var endpoint in specification.Endpoints)
        {
            endpoint.ServeAtRealPath = true;
        }

        var result = await SpecificationHandler.Handle(new SetSpecificationProviderMode(specification.Id, true), TestContext.Current.CancellationToken);

        Assert.Equal(["GET /pets/mine", "GET /pets/{id}"], result!.Served);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public async Task Reimport_KeepsProviderModeForOperationsStillInTheSpec()
    {
        var (specification, kept) = await AddSpecAsync("Petstore", "GET /pets", "GET /gone");
        kept.ServeAtRealPath = true;
        specification.Endpoints.Single(e => e.OperationKey == "GET /gone").ServeAtRealPath = true;

        var parser = new FakeSpecificationParser(new ParsedSpecification("Petstore", [new ParsedOperation("GET /pets", null), new ParsedOperation("POST /pets", null)]));
        await new ImportOpenApiSpecHandler(parser, _specifications, NullLogger<ImportOpenApiSpecHandler>.Instance)
            .Handle(new ImportOpenApiSpec("petstore.yaml", "openapi: 3.0.3"), TestContext.Current.CancellationToken);

        var reimported = await _specifications.FindByTitleAsync("Petstore", TestContext.Current.CancellationToken);
        Assert.True(reimported!.Endpoints.Single(e => e.OperationKey == "GET /pets").ServeAtRealPath);
        Assert.False(reimported.Endpoints.Single(e => e.OperationKey == "POST /pets").ServeAtRealPath);
    }

    private SetEndpointProviderModeHandler EndpointHandler => new(_specifications);

    private SetSpecificationProviderModeHandler SpecificationHandler => new(_specifications);

    private InvokeMockEndpointHandler MockHandler => new(_specifications, _callRecords, new SchemaValidator(), new ResponseTemplateEngine(TimeProvider.System), NullLogger<InvokeMockEndpointHandler>.Instance);

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
