using Mediator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VroksNet.Application.Provisioning;
using VroksNet.Application.Provisioning.ApplyProvisioning;

namespace VroksNet.Infrastructure.Provisioning;

/// <summary>
/// Applies the provisioning directory once at startup (ADR 0001) through <see cref="ApplyProvisioning"/>.
/// Registered after <c>DbWriteBackgroundService</c>, so the write queue is already being drained.
/// When it fails and <c>Provisioning:FailOnError</c> is true (the default), every error is logged
/// and the process stops with exit code 3 (docs/container-contract.md §7): a mock with half its
/// specs would make consumers' tests fail in confusing ways.
/// </summary>
public sealed class ProvisioningHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostApplicationLifetime lifetime,
    ProvisioningState state,
    ILogger<ProvisioningHostedService> logger) : BackgroundService
{
    public const int FailedExitCode = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ProvisioningReport report;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            report = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new ApplyProvisioning(), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Provisioning crashed.");
            report = new ProvisioningReport(ProvisioningStatus.Failed, null, null, ProvisioningCounts.None, [new ProvisioningError("provisioning", ex.Message)]);
            state.Set(report);
        }

        switch (report.Status)
        {
            case ProvisioningStatus.NotConfigured:
                logger.LogInformation("No provisioning directory; nothing to provision.");
                return;
            case ProvisioningStatus.Applied:
                logger.LogInformation(
                    "Provisioning applied from {Source}: {Specifications} specification(s), {Connections} connection(s), {Publishers} publisher(s), {TestScenarios} test scenario(s).",
                    report.Source, report.Counts.Specifications, report.Counts.Connections, report.Counts.Publishers, report.Counts.TestScenarios);
                return;
        }

        foreach (var error in report.Errors)
        {
            logger.LogError("Provisioning error in {Source}: {Message}", error.Source, error.Message);
        }

        if (configuration.GetValue("Provisioning:FailOnError", true))
        {
            logger.LogCritical("Provisioning failed with {Count} error(s); stopping (exit code {ExitCode}). Set Provisioning__FailOnError=false to keep running with what applied.",
                report.Errors.Count, FailedExitCode);
            Environment.ExitCode = FailedExitCode;
            lifetime.StopApplication();
        }
        else
        {
            logger.LogWarning("Provisioning failed with {Count} error(s); running with what applied (Provisioning:FailOnError is false).", report.Errors.Count);
        }
    }
}
