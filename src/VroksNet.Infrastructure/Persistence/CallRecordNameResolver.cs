using Microsoft.EntityFrameworkCore;
using VroksNet.Application.Abstractions;

namespace VroksNet.Infrastructure.Persistence;

/// <summary>Projection-only lookups (id + name columns) — never loads endpoint examples/schemas or whole entities.</summary>
public sealed class CallRecordNameResolver(IDbContextFactory<VroksNetDbContext> contextFactory) : ICallRecordNameResolver
{
    public async Task<CallRecordNames> ResolveAsync(
        IReadOnlyCollection<Guid> specificationIds,
        IReadOnlyCollection<Guid> mockEndpointIds,
        IReadOnlyCollection<Guid> connectionIds,
        IReadOnlyCollection<Guid> testScenarioIds,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var specificationTitles = specificationIds.Count == 0 ? [] : await context.ApiSpecifications
            .Where(specification => specificationIds.Contains(specification.Id))
            .ToDictionaryAsync(specification => specification.Id, specification => specification.Title, cancellationToken);

        var operationKeys = mockEndpointIds.Count == 0 ? [] : await context.MockEndpoints
            .Where(endpoint => mockEndpointIds.Contains(endpoint.Id))
            .ToDictionaryAsync(endpoint => endpoint.Id, endpoint => endpoint.OperationKey, cancellationToken);

        var connectionNames = connectionIds.Count == 0 ? [] : await context.Connections
            .Where(connection => connectionIds.Contains(connection.Id))
            .ToDictionaryAsync(connection => connection.Id, connection => connection.Name, cancellationToken);

        var testScenarioNames = testScenarioIds.Count == 0 ? [] : await context.TestScenarios
            .Where(scenario => testScenarioIds.Contains(scenario.Id))
            .ToDictionaryAsync(scenario => scenario.Id, scenario => scenario.Name, cancellationToken);

        return new CallRecordNames(specificationTitles, operationKeys, connectionNames, testScenarioNames);
    }
}
