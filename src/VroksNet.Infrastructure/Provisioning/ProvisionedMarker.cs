using Microsoft.EntityFrameworkCore;
using VroksNet.Application.Abstractions;
using VroksNet.Application.Provisioning;
using VroksNet.Infrastructure.Persistence;

namespace VroksNet.Infrastructure.Provisioning;

/// <summary>Sets <c>ProvisionedAt</c> on one row, through the write queue like every other write.</summary>
public sealed class ProvisionedMarker(IDbWriteQueue writeQueue) : IProvisionedMarker
{
    public Task MarkAsync(ProvisionedObject kind, Guid id, DateTimeOffset at, CancellationToken cancellationToken)
    {
        return writeQueue.EnqueueAsync(async (context, ct) =>
        {
            _ = kind switch
            {
                ProvisionedObject.Specification => await context.ApiSpecifications.Where(row => row.Id == id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ProvisionedAt, at), ct),
                ProvisionedObject.Connection => await context.Connections.Where(row => row.Id == id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ProvisionedAt, at), ct),
                ProvisionedObject.Publisher => await context.Publishers.Where(row => row.Id == id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ProvisionedAt, at), ct),
                ProvisionedObject.TestScenario => await context.TestScenarios.Where(row => row.Id == id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ProvisionedAt, at), ct),
                ProvisionedObject.TestSuite => await context.TestSuites.Where(row => row.Id == id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ProvisionedAt, at), ct),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
        }, cancellationToken);
    }
}
