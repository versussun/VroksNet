using VroksNet.Domain.Publishers;

namespace VroksNet.Application.Abstractions;

public interface IPublisherRepository
{
    Task<IReadOnlyList<Publisher>> ListAsync(CancellationToken cancellationToken);

    Task<Publisher?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task InsertAsync(Publisher publisher, CancellationToken cancellationToken);

    /// <summary>Saves edits to the publisher's own definition (not its last-publish outcome).</summary>
    /// <returns>False if no publisher with <see cref="Publisher.Id"/> exists.</returns>
    Task<bool> UpdateAsync(Publisher publisher, CancellationToken cancellationToken);

    /// <returns>False if no publisher with that id exists.</returns>
    Task<bool> SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken);

    /// <returns>False if no publisher with that id exists.</returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Records a publish's outcome — <see cref="Publisher.LastPublishedAt"/>/<see cref="Publisher.LastPublishSuccess"/>/<see cref="Publisher.LastPublishMessage"/> only.</summary>
    Task RecordPublishAsync(Guid id, DateTimeOffset publishedAt, bool success, string message, CancellationToken cancellationToken);
}
