using VroksNet.Application.Specifications.ListSpecifications;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.MockEndpoints;
using VroksNet.UnitTests.TestDoubles;

namespace VroksNet.UnitTests.Specifications;

public class ListSpecificationsHandlerTests
{
    [Fact]
    public async Task Handle_NoSpecifications_ReturnsEmptyList()
    {
        var handler = new ListSpecificationsHandler(new FakeApiSpecificationRepository());

        var result = await handler.Handle(new ListSpecifications(), TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_ReturnsSummariesOrderedByTitle_WithoutRawContent()
    {
        var repository = new FakeApiSpecificationRepository();
        await repository.UpsertAsync(CreateSpecification("Zebra API", 1), TestContext.Current.CancellationToken);
        await repository.UpsertAsync(CreateSpecification("Apple API", 2), TestContext.Current.CancellationToken);
        var handler = new ListSpecificationsHandler(repository);

        var result = await handler.Handle(new ListSpecifications(), TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Equal(["Apple API", "Zebra API"], result.Select(s => s.Title));
        Assert.Equal(2, result.Single(s => s.Title == "Apple API").EndpointCount);
    }

    private static ApiSpecification CreateSpecification(string title, int endpointCount)
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

        specification.Endpoints = Enumerable.Range(1, endpointCount)
            .Select(i => new MockEndpoint { Id = Guid.NewGuid(), SpecificationId = specification.Id, OperationKey = $"GET /op{i}" })
            .ToList();

        return specification;
    }
}
