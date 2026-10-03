using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Application.Connections.CreateConnection;
using VroksNet.Application.Connections.UpdateConnection;
using VroksNet.Application.Mocking.SetEndpointEnabled;
using VroksNet.Application.Mocking.SetSpecificationProviderMode;
using VroksNet.Application.Publishers.CreatePublisher;
using VroksNet.Application.Publishers.SetPublisherEnabled;
using VroksNet.Application.Publishers.UpdatePublisher;
using VroksNet.Application.Specifications.ImportAsyncApiSpec;
using VroksNet.Application.Specifications.ImportOpenApiSpec;
using VroksNet.Application.TestScenarios.CreateTestScenario;
using VroksNet.Application.TestScenarios.UpdateTestScenario;
using VroksNet.Application.TestSuites.CreateTestSuite;
using VroksNet.Application.TestSuites.UpdateTestSuite;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.Provisioning.ApplyProvisioning;

/// <summary>
/// Brings the database in line with the provisioning directory (ADR 0001), in dependency order:
/// specs, connections, spec settings, Publishers, test scenarios, test suites. Everything goes through the same
/// use cases as the UI and the API, so the same validation applies; objects are matched by name
/// (specs by title), so applying it again changes nothing that already matches — "the file wins".
/// <para>
/// One broken entry doesn't stop the rest: every failure becomes a <see cref="ProvisioningError"/>
/// and the report's status is <see cref="ProvisioningStatus.Failed"/>. Whether that stops the app
/// is the caller's decision (<c>Provisioning:FailOnError</c>). Objects that were provisioned before
/// but have since been removed from the manifest are left as they are.
/// </para>
/// </summary>
public sealed class ApplyProvisioningHandler(
    IProvisioningSource source,
    IMediator mediator,
    IApiSpecificationRepository specifications,
    IConnectionRepository connections,
    IPublisherRepository publishers,
    ITestScenarioRepository scenarios,
    ITestSuiteRepository suites,
    IProvisionedMarker marker,
    ProvisioningState state,
    TimeProvider timeProvider) : IRequestHandler<ApplyProvisioning, ProvisioningReport>
{
    public async ValueTask<ProvisioningReport> Handle(ApplyProvisioning request, CancellationToken cancellationToken)
    {
        var input = await source.LoadAsync(cancellationToken);
        if (input is null)
        {
            var notConfigured = new ProvisioningReport(ProvisioningStatus.NotConfigured, null, null, ProvisioningCounts.None, []);
            state.Set(notConfigured);
            return notConfigured;
        }

        state.Set(ProvisioningReport.NotStarted with { Source = input.Source });
        var run = new Run(input.Errors, timeProvider.GetUtcNow());

        foreach (var file in input.Specifications)
        {
            await run.TryAsync(file.RelativePath, async () =>
            {
                var id = file.Kind == SpecificationKind.AsyncApi
                    ? await mediator.Send(new ImportAsyncApiSpec(file.RelativePath, file.Content), cancellationToken)
                    : await mediator.Send(new ImportOpenApiSpec(file.RelativePath, file.Content), cancellationToken);
                await marker.MarkAsync(ProvisionedObject.Specification, id, run.At, cancellationToken);
                run.Specifications++;
            });
        }

        foreach (var connection in input.Manifest.Connections)
        {
            await run.TryAsync($"connections[{connection.Name}]", () => ApplyConnectionAsync(connection, run, cancellationToken));
        }

        foreach (var specification in input.Manifest.Specifications)
        {
            await run.TryAsync($"specifications[{specification.Title}]", () => ApplySpecificationSettingsAsync(specification, cancellationToken));
        }

        foreach (var publisher in input.Manifest.Publishers)
        {
            await run.TryAsync($"publishers[{publisher.Name}]", () => ApplyPublisherAsync(publisher, run, cancellationToken));
        }

        foreach (var scenario in input.Manifest.TestScenarios)
        {
            await run.TryAsync($"testScenarios[{scenario.Name}]", () => ApplyTestScenarioAsync(scenario, run, cancellationToken));
        }

        foreach (var suite in input.Manifest.TestSuites)
        {
            await run.TryAsync($"testSuites[{suite.Name}]", () => ApplyTestSuiteAsync(suite, run, cancellationToken));
        }

        var report = new ProvisioningReport(
            run.Errors.Count == 0 ? ProvisioningStatus.Applied : ProvisioningStatus.Failed,
            input.Source,
            run.At,
            new ProvisioningCounts(run.Specifications, run.Connections, run.Publishers, run.TestScenarios, run.TestSuites),
            run.Errors);
        state.Set(report);
        return report;
    }

    private async Task ApplyConnectionAsync(ManifestConnection connection, Run run, CancellationToken cancellationToken)
    {
        var existing = await connections.FindByNameAsync(connection.Name.Trim(), cancellationToken);
        var id = existing?.Id ?? await mediator.Send(new CreateConnection(connection.Name, connection.Type, connection.Value), cancellationToken);
        if (existing is not null)
        {
            await mediator.Send(new UpdateConnection(existing.Id, connection.Name, connection.Type, connection.Value), cancellationToken);
        }

        await marker.MarkAsync(ProvisionedObject.Connection, id, run.At, cancellationToken);
        run.Connections++;
    }

    private async Task ApplySpecificationSettingsAsync(ManifestSpecification settings, CancellationToken cancellationToken)
    {
        var specification = await FindSpecificationAsync(settings.Title, cancellationToken);

        var unknown = settings.DisabledOperations.Except(specification.Endpoints.Select(endpoint => endpoint.OperationKey), StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException($"disabledOperations lists operations the spec doesn't have: {string.Join(", ", unknown)}.");
        }

        foreach (var endpoint in specification.Endpoints.Where(endpoint => OperationCompatibility.IsHttpOperation(endpoint.OperationKey)))
        {
            var enabled = !settings.DisabledOperations.Contains(endpoint.OperationKey, StringComparer.Ordinal);
            if (endpoint.IsEnabled != enabled)
            {
                var result = await mediator.Send(new SetEndpointEnabled(endpoint.Id, enabled), cancellationToken);
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException($"{endpoint.OperationKey}: {result.Refusal ?? "not found"}");
                }
            }
        }

        var providerMode = await mediator.Send(new SetSpecificationProviderMode(specification.Id, settings.ProviderMode), cancellationToken);
        if (providerMode?.Refusal is { } refusal)
        {
            throw new InvalidOperationException(refusal);
        }

        if (settings.ProviderMode && providerMode is { Skipped.Count: > 0 })
        {
            throw new InvalidOperationException("Not every operation could be served at its real path: "
                + string.Join("; ", providerMode.Skipped.Select(skipped => $"{skipped.OperationKey} — {skipped.Reason}")));
        }
    }

    private async Task ApplyPublisherAsync(ManifestPublisher publisher, Run run, CancellationToken cancellationToken)
    {
        var (specificationId, endpointId) = await ResolveOperationAsync(publisher.Specification, publisher.Operation, cancellationToken);
        var connectionId = await ResolveConnectionAsync(publisher.Connection, cancellationToken);

        var existing = await publishers.FindByNameAsync(publisher.Name.Trim(), cancellationToken);
        Guid id;
        if (existing is null)
        {
            id = await mediator.Send(new CreatePublisher(
                Name: publisher.Name,
                SpecificationId: specificationId,
                MockEndpointId: endpointId,
                ConnectionId: connectionId,
                PayloadOverride: publisher.PayloadOverride,
                IntervalSeconds: publisher.IntervalSeconds,
                Exchange: publisher.Exchange,
                Enabled: publisher.Enabled,
                BrokerOptions: publisher.BrokerOptions), cancellationToken);
        }
        else
        {
            id = existing.Id;
            await mediator.Send(new UpdatePublisher(
                Id: existing.Id,
                Name: publisher.Name,
                SpecificationId: specificationId,
                MockEndpointId: endpointId,
                ConnectionId: connectionId,
                PayloadOverride: publisher.PayloadOverride,
                IntervalSeconds: publisher.IntervalSeconds,
                Exchange: publisher.Exchange,
                BrokerOptions: publisher.BrokerOptions), cancellationToken);
            if (existing.IsEnabled != publisher.Enabled)
            {
                await mediator.Send(new SetPublisherEnabled(existing.Id, publisher.Enabled), cancellationToken);
            }
        }

        await marker.MarkAsync(ProvisionedObject.Publisher, id, run.At, cancellationToken);
        run.Publishers++;
    }

    private async Task ApplyTestScenarioAsync(ManifestTestScenario scenario, Run run, CancellationToken cancellationToken)
    {
        var (specificationId, endpointId) = await ResolveOperationAsync(scenario.Specification, scenario.Operation, cancellationToken);
        var connectionId = await ResolveConnectionAsync(scenario.Connection, cancellationToken);
        var kind = scenario.Kind ?? TestScenarioListening.DefaultKindFor(scenario.Operation);

        var existing = await scenarios.FindByNameAsync(scenario.Name.Trim(), cancellationToken);
        Guid id;
        if (existing is null)
        {
            id = await mediator.Send(new CreateTestScenario(
                Name: scenario.Name,
                SpecificationId: specificationId,
                MockEndpointId: endpointId,
                ConnectionId: connectionId,
                PayloadOverride: scenario.PayloadOverride,
                Kind: kind,
                ListenTimeoutSeconds: scenario.ListenTimeoutSeconds,
                Exchange: scenario.Exchange,
                Schedule: scenario.Schedule,
                ScheduleTimeZone: scenario.ScheduleTimeZone,
                BrokerOptions: scenario.BrokerOptions), cancellationToken);
        }
        else
        {
            id = existing.Id;
            await mediator.Send(new UpdateTestScenario(
                Id: existing.Id,
                Name: scenario.Name,
                SpecificationId: specificationId,
                MockEndpointId: endpointId,
                ConnectionId: connectionId,
                PayloadOverride: scenario.PayloadOverride,
                Kind: kind,
                ListenTimeoutSeconds: scenario.ListenTimeoutSeconds,
                Exchange: scenario.Exchange,
                Schedule: scenario.Schedule,
                ScheduleTimeZone: scenario.ScheduleTimeZone,
                BrokerOptions: scenario.BrokerOptions), cancellationToken);
        }

        await marker.MarkAsync(ProvisionedObject.TestScenario, id, run.At, cancellationToken);
        run.TestScenarios++;
    }

    private async Task ApplyTestSuiteAsync(ManifestTestSuite suite, Run run, CancellationToken cancellationToken)
    {
        var scenarioIds = new List<Guid>(suite.Scenarios.Count);
        foreach (var name in suite.Scenarios)
        {
            scenarioIds.Add((await scenarios.FindByNameAsync(name.Trim(), cancellationToken))?.Id
                ?? throw new InvalidOperationException($"No test scenario named \"{name}\"."));
        }

        var existing = await suites.FindByNameAsync(suite.Name.Trim(), cancellationToken);
        Guid id;
        if (existing is null)
        {
            id = await mediator.Send(new CreateTestSuite(suite.Name, scenarioIds, suite.RunOnStartup), cancellationToken);
        }
        else
        {
            id = existing.Id;
            await mediator.Send(new UpdateTestSuite(existing.Id, suite.Name, scenarioIds, suite.RunOnStartup), cancellationToken);
        }

        await marker.MarkAsync(ProvisionedObject.TestSuite, id, run.At, cancellationToken);
        run.TestSuites++;
    }

    private async Task<ApiSpecification> FindSpecificationAsync(string title, CancellationToken cancellationToken)
        => await specifications.FindByTitleAsync(title, cancellationToken)
            ?? throw new InvalidOperationException($"No specification titled \"{title}\" — is its file in specs/?");

    /// <summary>The spec by title and its operation by key; with repeated keys (AsyncAPI), the first in the spec.</summary>
    private async Task<(Guid SpecificationId, Guid EndpointId)> ResolveOperationAsync(string title, string operationKey, CancellationToken cancellationToken)
    {
        var specification = await FindSpecificationAsync(title, cancellationToken);
        var endpoint = specification.Endpoints
            .Where(endpoint => string.Equals(endpoint.OperationKey, operationKey, StringComparison.Ordinal))
            .OrderBy(endpoint => endpoint.Position)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Specification \"{title}\" has no operation \"{operationKey}\".");
        return (specification.Id, endpoint.Id);
    }

    private async Task<Guid> ResolveConnectionAsync(string name, CancellationToken cancellationToken)
        => (await connections.FindByNameAsync(name.Trim(), cancellationToken))?.Id
            ?? throw new InvalidOperationException($"No connection named \"{name}\".");

    /// <summary>One application's running tally.</summary>
    private sealed class Run(IReadOnlyList<ProvisioningError> readErrors, DateTimeOffset at)
    {
        public DateTimeOffset At { get; } = at;

        public List<ProvisioningError> Errors { get; } = [.. readErrors];

        public int Specifications { get; set; }

        public int Connections { get; set; }

        public int Publishers { get; set; }

        public int TestScenarios { get; set; }

        public int TestSuites { get; set; }

        /// <summary>Runs one entry; a failure is recorded against <paramref name="source"/> and the rest carry on.</summary>
        public async Task TryAsync(string source, Func<Task> apply)
        {
            try
            {
                await apply();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Errors.Add(new ProvisioningError(source, ex.Message));
            }
        }
    }
}
