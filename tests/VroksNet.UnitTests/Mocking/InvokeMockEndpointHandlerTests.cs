using VroksNet.Application.Mocking.InvokeMockEndpoint;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.MockEndpoints;
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
        var handler = new InvokeMockEndpointHandler(repository);

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
        var handler = new InvokeMockEndpointHandler(repository);

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
        var handler = new InvokeMockEndpointHandler(repository);

        var result = await handler.Handle(new InvokeMockEndpoint("GET", "/pets"), TestContext.Current.CancellationToken);

        Assert.False(result.Matched);
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
