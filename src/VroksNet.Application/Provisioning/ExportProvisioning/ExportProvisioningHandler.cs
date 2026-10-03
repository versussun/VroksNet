using System.Text.RegularExpressions;
using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.Provisioning.ExportProvisioning;

/// <summary>
/// Builds the manifest the provisioning handler would turn back into the same objects: everything
/// is referred to by name (specs by title, operations by key), so applying the export to an empty
/// instance recreates the configuration. Specs export their imported content as it is.
/// </summary>
public sealed partial class ExportProvisioningHandler(
    IApiSpecificationRepository specifications,
    IConnectionRepository connections,
    IPublisherRepository publishers,
    ITestScenarioRepository scenarios,
    ITestSuiteRepository suites,
    IProvisioningPackageWriter writer,
    IAppVersionProvider version,
    TimeProvider timeProvider) : IRequestHandler<ExportProvisioning, byte[]>
{
    public async ValueTask<byte[]> Handle(ExportProvisioning request, CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        var specs = await specifications.ListAsync(cancellationToken);
        var specsById = specs.ToDictionary(spec => spec.Id);
        var allConnections = await connections.ListAsync(cancellationToken);
        var connectionsById = allConnections.ToDictionary(connection => connection.Id);

        var exportedConnections = ExportConnections(allConnections, request.InlineConnectionValues);
        var exportedSpecs = ExportSpecifications(specs);
        var settings = specs.Select(spec => SettingsOf(spec, notes)).OfType<ManifestSpecification>().ToList();

        // A reference that no longer resolves would fail the import, so the object is left out.
        string? Missing(Guid specificationId, Guid endpointId, Guid connectionId, out string? operation)
        {
            operation = specsById.GetValueOrDefault(specificationId)?.Endpoints.FirstOrDefault(endpoint => endpoint.Id == endpointId)?.OperationKey;
            return !specsById.ContainsKey(specificationId) ? "its specification"
                : operation is null ? "its operation"
                : !connectionsById.ContainsKey(connectionId) ? "its connection"
                : null;
        }

        var exportedPublishers = new List<ManifestPublisher>();
        foreach (var publisher in await publishers.ListAsync(cancellationToken))
        {
            if (Missing(publisher.SpecificationId, publisher.MockEndpointId, publisher.ConnectionId, out var operation) is { } missing)
            {
                notes.Add($"Publisher \"{publisher.Name}\" isn't exported: {missing} no longer exists.");
                continue;
            }

            exportedPublishers.Add(new ManifestPublisher(
                publisher.Name, specsById[publisher.SpecificationId].Title, operation!, connectionsById[publisher.ConnectionId].Name,
                publisher.IntervalSeconds, publisher.Exchange, publisher.PayloadOverride, publisher.IsEnabled));
        }

        var allScenarios = await scenarios.ListAsync(cancellationToken);
        var exportedScenarios = new List<ManifestTestScenario>();
        var exportedScenarioIds = new HashSet<Guid>();
        foreach (var scenario in allScenarios)
        {
            if (Missing(scenario.SpecificationId, scenario.MockEndpointId, scenario.ConnectionId, out var operation) is { } missing)
            {
                notes.Add($"Test scenario \"{scenario.Name}\" isn't exported: {missing} no longer exists.");
                continue;
            }

            exportedScenarios.Add(new ManifestTestScenario(
                scenario.Name, specsById[scenario.SpecificationId].Title, operation!, connectionsById[scenario.ConnectionId].Name,
                scenario.Kind,
                scenario.Kind == TestScenarioKind.Listen ? scenario.ListenTimeoutSeconds : null,
                scenario.Exchange, scenario.PayloadOverride, scenario.Schedule, scenario.ScheduleTimeZone));
            exportedScenarioIds.Add(scenario.Id);
        }

        var scenarioNames = allScenarios.ToDictionary(scenario => scenario.Id, scenario => scenario.Name);
        var exportedSuites = new List<ManifestTestSuite>();
        foreach (var suite in await suites.ListAsync(cancellationToken))
        {
            var kept = suite.TestScenarioIds.Where(exportedScenarioIds.Contains).Select(id => scenarioNames[id]).ToList();
            if (kept.Count < suite.TestScenarioIds.Count)
            {
                notes.Add(kept.Count == 0
                    ? $"Test suite \"{suite.Name}\" isn't exported: none of its scenarios is."
                    : $"Test suite \"{suite.Name}\" is exported without {suite.TestScenarioIds.Count - kept.Count} scenario(s) that aren't.");
            }

            if (kept.Count > 0)
            {
                exportedSuites.Add(new ManifestTestSuite(suite.Name, kept, suite.RunOnStartup));
            }
        }

        var export = new ProvisioningExport(
            version.Version, timeProvider.GetUtcNow(), exportedSpecs, exportedConnections, settings,
            exportedPublishers, exportedScenarios, exportedSuites, notes);
        return writer.Write(export);
    }

    /// <summary>
    /// Each connection's value becomes <c>valueFrom: ConnectionStrings:&lt;name&gt;</c> — the name
    /// reduced to characters an environment variable can carry — unless values are inlined.
    /// </summary>
    private static List<ExportedConnection> ExportConnections(IReadOnlyList<Connection> all, bool inlineValues)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return all.Select(connection =>
        {
            if (inlineValues)
            {
                return new ExportedConnection(connection.Name, connection.ServiceType, connection.Value, null);
            }

            var key = Unique(UnsafeKeyCharacters().Replace(connection.Name, "_"), keys);
            return new ExportedConnection(connection.Name, connection.ServiceType, null, $"ConnectionStrings:{key}");
        }).ToList();
    }

    /// <summary><c>specs/&lt;title as a file name&gt;.yaml</c> (or <c>.json</c> for a JSON document), made unique.</summary>
    private static List<ExportedSpecification> ExportSpecifications(IReadOnlyList<ApiSpecification> all)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return all.Select(spec =>
        {
            var stem = UnsafeFileCharacters().Replace(spec.Title.ToLowerInvariant(), "-").Trim('-');
            var extension = spec.RawContent.TrimStart().StartsWith('{') ? ".json" : ".yaml";
            return new ExportedSpecification(Unique(stem.Length == 0 ? "specification" : stem, names) + extension, spec.RawContent);
        }).ToList();
    }

    /// <summary>The spec's settings, or null when they're the defaults (every operation enabled, provider mode off).</summary>
    private static ManifestSpecification? SettingsOf(ApiSpecification spec, List<string> notes)
    {
        var http = spec.Endpoints.Where(endpoint => OperationCompatibility.IsHttpOperation(endpoint.OperationKey)).ToList();
        var disabled = http.Where(endpoint => !endpoint.IsEnabled).Select(endpoint => endpoint.OperationKey).Distinct().ToList();
        var atRealPath = http.Count(endpoint => endpoint.ServeAtRealPath);
        var providerMode = atRealPath > 0;
        if (providerMode && atRealPath < http.Count)
        {
            notes.Add($"\"{spec.Title}\" serves only some operations at their real paths; the manifest can only say all or none, so it's exported as providerMode: true.");
        }

        return disabled.Count == 0 && !providerMode ? null : new ManifestSpecification(spec.Title, providerMode, disabled);
    }

    private static string Unique(string candidate, HashSet<string> taken)
    {
        var result = candidate;
        for (var i = 2; !taken.Add(result); i++)
        {
            result = $"{candidate}-{i}";
        }

        return result;
    }

    [GeneratedRegex("[^A-Za-z0-9_-]")]
    private static partial Regex UnsafeKeyCharacters();

    [GeneratedRegex("[^a-z0-9._-]+")]
    private static partial Regex UnsafeFileCharacters();
}
