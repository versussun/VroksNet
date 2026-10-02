using Microsoft.Extensions.Logging.Abstractions;
using VroksNet.Application.CallRecords;
using VroksNet.Application.Mocking.InvokeMockEndpoint;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.CallRecords;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Infrastructure.SchemaValidation;
using VroksNet.Infrastructure.Templating;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.Mocking;

public class InvokeMockEndpointHandlerTests
{
    [Fact]
    public async Task Handle_MatchingEnabledEndpointWithExample_ReturnsIt()
    {
        var repository = new FakeApiSpecificationRepository();
        await repository.UpsertAsync(CreateSpecification("Petstore Sample API",
            new MockEndpoint { Id = Guid.NewGuid(), OperationKey = "GET /pets/{petId}", IsEnabled = true, ExampleTemplate = "{\"id\":1}" }),
            TestContext.Current.CancellationToken);
        var handler = new InvokeMockEndpointHandler(repository, new FakeCallRecordRepository(), new SchemaValidator(), new ResponseTemplateEngine(TimeProvider.System), NullLogger<InvokeMockEndpointHandler>.Instance);

        var result = await handler.Handle(new InvokeMockEndpoint("GET", "/pets/1"), TestContext.Current.CancellationToken);

        Assert.True(result.Matched);
        Assert.Equal("GET /pets/{petId}", result.OperationKey);
        Assert.Equal("{\"id\":1}", result.ExampleJson);
    }

    [Fact]
    public async Task Handle_NoMatchingEndpoint_ReturnsNotMatched()
    {
        var repository = new FakeApiSpecificationRepository();
        await repository.UpsertAsync(CreateSpecification("Petstore Sample API",
            new MockEndpoint { Id = Guid.NewGuid(), OperationKey = "GET /pets", IsEnabled = true }),
            TestContext.Current.CancellationToken);
        var handler = new InvokeMockEndpointHandler(repository, new FakeCallRecordRepository(), new SchemaValidator(), new ResponseTemplateEngine(TimeProvider.System), NullLogger<InvokeMockEndpointHandler>.Instance);

        var result = await handler.Handle(new InvokeMockEndpoint("GET", "/orders"), TestContext.Current.CancellationToken);

        Assert.False(result.Matched);
        Assert.Null(result.OperationKey);
        Assert.Null(result.ExampleJson);
    }

    [Fact]
    public async Task Handle_DisabledEndpoint_IsIgnored()
    {
        var repository = new FakeApiSpecificationRepository();
        await repository.UpsertAsync(CreateSpecification("Petstore Sample API",
            new MockEndpoint { Id = Guid.NewGuid(), OperationKey = "GET /pets", IsEnabled = false, ExampleTemplate = "[]" }),
            TestContext.Current.CancellationToken);
        var handler = new InvokeMockEndpointHandler(repository, new FakeCallRecordRepository(), new SchemaValidator(), new ResponseTemplateEngine(TimeProvider.System), NullLogger<InvokeMockEndpointHandler>.Instance);

        var result = await handler.Handle(new InvokeMockEndpoint("GET", "/pets"), TestContext.Current.CancellationToken);

        Assert.False(result.Matched);
    }

    [Fact]
    public async Task Handle_MatchedCall_IsLoggedWithWhatWasServed()
    {
        var repository = new FakeApiSpecificationRepository();
        var endpoint = new MockEndpoint { Id = Guid.NewGuid(), OperationKey = "POST /pets", IsEnabled = true };
        var specification = CreateSpecification("Petstore Sample API", endpoint);
        await repository.UpsertAsync(specification, TestContext.Current.CancellationToken);
        var callRecords = new FakeCallRecordRepository();
        var handler = new InvokeMockEndpointHandler(repository, callRecords, new SchemaValidator(), new ResponseTemplateEngine(TimeProvider.System), NullLogger<InvokeMockEndpointHandler>.Instance);

        var result = await handler.Handle(new InvokeMockEndpoint("POST", "/pets", "?dryRun=true", """{"name":"Fido"}"""), TestContext.Current.CancellationToken);

        // No example in the spec — the "{}" stand-in is what's served, and what's logged.
        Assert.Equal("{}", result.ResponseBody);

        var logged = Assert.Single(callRecords.Inserted);
        Assert.Equal(CallDirection.InboundHttpRequest, logged.Direction);
        Assert.Equal(specification.Id, logged.SpecificationId);
        Assert.Equal(endpoint.Id, logged.MockEndpointId);
        Assert.Equal("POST /mock/pets?dryRun=true", logged.RequestLine);
        Assert.Equal("""{"name":"Fido"}""", logged.RequestSnapshot);
        Assert.Equal("{}", logged.ResponseSnapshot);
        Assert.Equal(200, logged.StatusCode);
        Assert.Null(logged.ContractValid);
    }

    [Fact]
    public async Task Handle_UnmatchedCall_IsStillLoggedAs404WithItsRequestLine()
    {
        var callRecords = new FakeCallRecordRepository();
        var handler = new InvokeMockEndpointHandler(new FakeApiSpecificationRepository(), callRecords, new SchemaValidator(), new ResponseTemplateEngine(TimeProvider.System), NullLogger<InvokeMockEndpointHandler>.Instance);

        var result = await handler.Handle(new InvokeMockEndpoint("GET", "/orders/7"), TestContext.Current.CancellationToken);

        Assert.False(result.Matched);
        var logged = Assert.Single(callRecords.Inserted);
        Assert.Null(logged.SpecificationId);
        Assert.Null(logged.MockEndpointId);
        Assert.Equal("GET /mock/orders/7", logged.RequestLine);
        Assert.Equal(404, logged.StatusCode);
        Assert.Equal(result.ResponseBody, logged.ResponseSnapshot);
    }

    [Fact]
    public async Task Handle_HistoryWriteFails_StillAnswersTheMockCall()
    {
        var repository = new FakeApiSpecificationRepository();
        await repository.UpsertAsync(CreateSpecification("Petstore Sample API",
            new MockEndpoint { Id = Guid.NewGuid(), OperationKey = "GET /pets", IsEnabled = true, ExampleTemplate = "[]" }),
            TestContext.Current.CancellationToken);
        var callRecords = new FakeCallRecordRepository { InsertFailure = new InvalidOperationException("database is locked") };
        var handler = new InvokeMockEndpointHandler(repository, callRecords, new SchemaValidator(), new ResponseTemplateEngine(TimeProvider.System), NullLogger<InvokeMockEndpointHandler>.Instance);

        var result = await handler.Handle(new InvokeMockEndpoint("GET", "/pets"), TestContext.Current.CancellationToken);

        Assert.True(result.Matched);
        Assert.Equal("[]", result.ResponseBody);
    }

    [Fact]
    public async Task Handle_OversizedBody_IsTruncatedInTheHistory()
    {
        var callRecords = new FakeCallRecordRepository();
        var handler = new InvokeMockEndpointHandler(new FakeApiSpecificationRepository(), callRecords, new SchemaValidator(), new ResponseTemplateEngine(TimeProvider.System), NullLogger<InvokeMockEndpointHandler>.Instance);

        await handler.Handle(new InvokeMockEndpoint("POST", "/big", Body: new string('x', CallRecordSnapshot.MaxLength + 1)), TestContext.Current.CancellationToken);

        var snapshot = Assert.Single(callRecords.Inserted).RequestSnapshot!;
        Assert.Equal(CallRecordSnapshot.MaxLength + CallRecordSnapshot.TruncatedMarker.Length, snapshot.Length);
        Assert.EndsWith(CallRecordSnapshot.TruncatedMarker, snapshot);
    }

    [Fact]
    public async Task Handle_ExampleTemplate_IsRenderedWithRequestValuesAtTheSpecStatus()
    {
        var repository = new FakeApiSpecificationRepository();
        await repository.UpsertAsync(CreateSpecification("Petstore Sample API",
            new MockEndpoint
            {
                Id = Guid.NewGuid(),
                OperationKey = "POST /owners/{ownerId}/pets",
                IsEnabled = true,
                ExampleStatusCode = 201,
                ExampleTemplate = """{"owner": "{{request.path.ownerId}}", "name": {{request.body.$.name}}, "lang": "{{request.query.lang}}", "trace": "{{request.header.X-Trace}}"}"""
            }),
            TestContext.Current.CancellationToken);
        var callRecords = new FakeCallRecordRepository();
        var handler = new InvokeMockEndpointHandler(repository, callRecords, new SchemaValidator(), new ResponseTemplateEngine(TimeProvider.System), NullLogger<InvokeMockEndpointHandler>.Instance);

        var result = await handler.Handle(
            new InvokeMockEndpoint("POST", "/owners/7/pets", "?lang=uk", """{"name":"Fido"}""",
                QueryParameters: new Dictionary<string, string> { ["lang"] = "uk" },
                Headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["x-trace"] = "t-1" }),
            TestContext.Current.CancellationToken);

        Assert.Equal(201, result.StatusCode);
        Assert.Equal("""{"owner": "7", "name": "Fido", "lang": "uk", "trace": "t-1"}""", result.ResponseBody);
        var logged = Assert.Single(callRecords.Inserted);
        Assert.Equal(201, logged.StatusCode);
        Assert.Equal(result.ResponseBody, logged.ResponseSnapshot);
        Assert.Null(logged.Warnings);
    }

    [Fact]
    public async Task Handle_UnresolvablePlaceholder_StillAnswersAndLogsAWarning()
    {
        var repository = new FakeApiSpecificationRepository();
        await repository.UpsertAsync(CreateSpecification("Petstore Sample API",
            new MockEndpoint { Id = Guid.NewGuid(), OperationKey = "GET /pets", IsEnabled = true, ExampleTemplate = """{"id": {{request.query.id}}}""" }),
            TestContext.Current.CancellationToken);
        var callRecords = new FakeCallRecordRepository();
        var handler = new InvokeMockEndpointHandler(repository, callRecords, new SchemaValidator(), new ResponseTemplateEngine(TimeProvider.System), NullLogger<InvokeMockEndpointHandler>.Instance);

        var result = await handler.Handle(new InvokeMockEndpoint("GET", "/pets"), TestContext.Current.CancellationToken);

        // Imported before statuses were tracked — 200.
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("""{"id": null}""", result.ResponseBody);
        var warnings = Assert.Single(callRecords.Inserted).Warnings;
        Assert.NotNull(warnings);
        Assert.Contains("{{request.query.id}}", warnings);
    }

    [Fact]
    public async Task Handle_NoContentStatus_AnswersWithAnEmptyBody()
    {
        var repository = new FakeApiSpecificationRepository();
        await repository.UpsertAsync(CreateSpecification("Petstore Sample API",
            new MockEndpoint { Id = Guid.NewGuid(), OperationKey = "DELETE /pets/{id}", IsEnabled = true, ExampleStatusCode = 204 }),
            TestContext.Current.CancellationToken);
        var handler = new InvokeMockEndpointHandler(repository, new FakeCallRecordRepository(), new SchemaValidator(), new ResponseTemplateEngine(TimeProvider.System), NullLogger<InvokeMockEndpointHandler>.Instance);

        var result = await handler.Handle(new InvokeMockEndpoint("DELETE", "/pets/1"), TestContext.Current.CancellationToken);

        Assert.Equal(204, result.StatusCode);
        Assert.Equal(string.Empty, result.ResponseBody);
    }

    private static ApiSpecification CreateSpecification(string title, params MockEndpoint[] endpoints)
    {
        var specification = new ApiSpecification
        {
            Id = Guid.NewGuid(),
            Title = title,
            Kind = SpecificationKind.OpenApi,
            RawContent = "raw",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        foreach (var endpoint in endpoints)
        {
            endpoint.SpecificationId = specification.Id;
        }

        specification.Endpoints = endpoints;
        return specification;
    }
}
