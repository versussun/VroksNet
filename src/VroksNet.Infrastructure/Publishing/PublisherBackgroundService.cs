using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VroksNet.Application.Publishers.ListDuePublishers;
using VroksNet.Application.Publishers.PublishNow;

namespace VroksNet.Infrastructure.Publishing;

/// <summary>
/// The async-mock worker (docs/project-brief.md, Phase 03): once a second, asks Application which
/// enabled publishers are due and publishes each through <see cref="PublishNow"/> — the same use
/// case as the on-demand button, so scheduling is the only thing that lives here. Due publishers
/// run concurrently, and the next tick waits for all of them, so one publisher is never published
/// twice at once. A failing tick is logged and the loop carries on.
/// </summary>
public sealed class PublisherBackgroundService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<PublisherBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Tick, timeProvider);
        try
        {
            do
            {
                try
                {
                    await PublishDueAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Publishing the due publishers failed.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task PublishDueAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var due = await mediator.Send(new ListDuePublishers(timeProvider.GetUtcNow()), cancellationToken);
        await Task.WhenAll(due.Select(async id =>
        {
            try
            {
                await mediator.Send(new PublishNow(id), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Publisher {PublisherId} failed to publish.", id);
            }
        }));
    }
}
