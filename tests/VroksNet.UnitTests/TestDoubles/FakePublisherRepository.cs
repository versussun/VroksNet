using VroksNet.Application.Abstractions;
using VroksNet.Domain.Publishers;

namespace VroksNet.UnitTests.TestDoubles;

internal sealed class FakePublisherRepository : IPublisherRepository
{
    private readonly List<Publisher> _publishers = [];

    public Task<IReadOnlyList<Publisher>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Publisher>>(_publishers.ToList());

    public Task<Publisher?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_publishers.FirstOrDefault(p => p.Id == id));

    public Task<Publisher?> FindByNameAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(_publishers.FirstOrDefault(p => p.Name == name));

    public Task InsertAsync(Publisher publisher, CancellationToken cancellationToken)
    {
        _publishers.Add(publisher);
        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(Publisher publisher, CancellationToken cancellationToken)
    {
        var index = _publishers.FindIndex(p => p.Id == publisher.Id);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        _publishers[index] = publisher;
        return Task.FromResult(true);
    }

    public Task<bool> SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken)
    {
        var publisher = _publishers.FirstOrDefault(p => p.Id == id);
        if (publisher is null)
        {
            return Task.FromResult(false);
        }

        publisher.IsEnabled = enabled;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_publishers.RemoveAll(p => p.Id == id) > 0);

    public Task RecordPublishAsync(Guid id, DateTimeOffset publishedAt, bool success, string message, CancellationToken cancellationToken)
    {
        var publisher = _publishers.FirstOrDefault(p => p.Id == id);
        if (publisher is not null)
        {
            publisher.LastPublishedAt = publishedAt;
            publisher.LastPublishSuccess = success;
            publisher.LastPublishMessage = message;
        }

        return Task.CompletedTask;
    }
}
