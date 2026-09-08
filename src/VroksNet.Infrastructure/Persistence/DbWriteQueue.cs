using System.Threading.Channels;

namespace VroksNet.Infrastructure.Persistence;

/// <summary>
/// Channel-backed implementation of <see cref="IDbWriteQueue"/>. Registered as a singleton;
/// <see cref="DbWriteBackgroundService"/> is the single reader that actually executes writes.
/// </summary>
public sealed class DbWriteQueue : IDbWriteQueue
{
    private readonly Channel<WriteJob> _channel = Channel.CreateUnbounded<WriteJob>(new UnboundedChannelOptions
    {
        SingleReader = true
    });

    internal ChannelReader<WriteJob> Reader => _channel.Reader;

    public async Task EnqueueAsync(Func<VroksNetDbContext, CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        var job = new WriteJob(operation, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        await _channel.Writer.WriteAsync(job, cancellationToken);
        await job.Completion.Task;
    }

    internal sealed record WriteJob(Func<VroksNetDbContext, CancellationToken, Task> Operation, TaskCompletionSource Completion);
}
