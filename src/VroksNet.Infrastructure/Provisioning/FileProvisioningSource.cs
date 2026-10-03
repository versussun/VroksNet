using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Json.Schema;
using Microsoft.Extensions.Configuration;
using VroksNet.Application.Abstractions;
using VroksNet.Application.Provisioning;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.Connections;
using VroksNet.Domain.TestScenarios;
using VroksNet.Infrastructure.Yaml;

namespace VroksNet.Infrastructure.Provisioning;

/// <summary>
/// Reads the provisioning directory (<c>Provisioning:Path</c>, default <c>/app/provisioning</c>;
/// docs/container-contract.md §4):
/// <list type="bullet">
/// <item><c>specs/**/*.{yaml,yml,json}</c> — the kind comes from the document's root key
/// (<c>openapi</c>/<c>swagger</c> or <c>asyncapi</c>), not the file name;</item>
/// <item><c>vroksnet.yaml</c> (optional) — validated against the embedded
/// provisioning-manifest.v1 schema, then each connection's <c>valueFrom</c> is read from
/// configuration (e.g. <c>ConnectionStrings:kafka</c>, set by Aspire's WithReference).</item>
/// <item>connections declared in configuration, <c>Provisioning:Connections:&lt;i&gt;:Name/Type/Value/ValueFrom</c>
/// — how the Aspire package passes <c>WithConnection(...)</c> without writing files. They're merged
/// with the manifest's; the same name in both is an error.</item>
/// </list>
/// Provisioning is configured when the directory exists or connections are declared in
/// configuration; with neither there's nothing to do.
/// Problems are collected, never thrown, and never quote a connection value.
/// </summary>
public sealed class FileProvisioningSource(IConfiguration configuration) : IProvisioningSource
{
    public const string DefaultPath = "/app/provisioning";
    private const string ManifestFileName = "vroksnet.yaml";
    private const string SchemaResource = "VroksNet.Infrastructure.Provisioning.provisioning-manifest.v1.schema.json";

    private static readonly string[] SpecExtensions = [".yaml", ".yml", ".json"];
    private static readonly Lazy<JsonSchema> ManifestSchema = new(LoadSchema);
    private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<ProvisioningInput?> LoadAsync(CancellationToken cancellationToken)
    {
        var root = configuration["Provisioning:Path"] is { Length: > 0 } configured ? configured : DefaultPath;
        var hasDirectory = Directory.Exists(root);
        var configuredConnections = configuration.GetSection("Provisioning:Connections").GetChildren().ToList();
        if (!hasDirectory && configuredConnections.Count == 0)
        {
            return null;
        }

        var errors = new List<ProvisioningError>();
        var specifications = hasDirectory ? await ReadSpecificationsAsync(root, errors, cancellationToken) : [];
        var manifest = hasDirectory ? await ReadManifestAsync(root, errors, cancellationToken) : ProvisioningManifest.Empty;

        var connections = manifest.Connections.ToList();
        foreach (var connection in ReadConfiguredConnections(configuredConnections, errors))
        {
            if (connections.Any(existing => string.Equals(existing.Name.Trim(), connection.Name.Trim(), StringComparison.Ordinal)))
            {
                errors.Add(new ProvisioningError($"connections[{connection.Name}]", "Declared both in vroksnet.yaml and in Provisioning__Connections__* — keep one."));
                continue;
            }

            connections.Add(connection);
        }

        var source = hasDirectory ? root : "configuration";
        return new ProvisioningInput(source, specifications, manifest with { Connections = connections }, errors);
    }

    /// <summary>
    /// <c>Provisioning:Connections:&lt;i&gt;</c> entries (docs/container-contract.md §5), in index
    /// order. Errors name the environment-variable form, which is how they're usually set.
    /// </summary>
    private IEnumerable<ManifestConnection> ReadConfiguredConnections(List<IConfigurationSection> sections, List<ProvisioningError> errors)
    {
        foreach (var section in sections.OrderBy(section => int.TryParse(section.Key, out var index) ? index : int.MaxValue).ThenBy(section => section.Key, StringComparer.Ordinal))
        {
            var source = $"Provisioning__Connections__{section.Key}";
            var name = section["Name"];
            var type = section["Type"];
            var value = section["Value"];
            var valueFrom = section["ValueFrom"];

            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add(new ProvisioningError(source, "Name is required."));
                continue;
            }

            if (!Enum.TryParse<ConnectionServiceType>(type, ignoreCase: true, out var serviceType) || !Enum.IsDefined(serviceType))
            {
                errors.Add(new ProvisioningError(source, $"Type must be one of {string.Join(", ", Enum.GetNames<ConnectionServiceType>())}."));
                continue;
            }

            if (string.IsNullOrEmpty(value) == string.IsNullOrEmpty(valueFrom))
            {
                errors.Add(new ProvisioningError(source, "Set exactly one of Value and ValueFrom."));
                continue;
            }

            var resolved = string.IsNullOrEmpty(value) ? configuration[valueFrom!] : value;
            if (string.IsNullOrWhiteSpace(resolved))
            {
                errors.Add(new ProvisioningError(source, $"ValueFrom \"{valueFrom}\" isn't set in the configuration."));
                continue;
            }

            yield return new ManifestConnection(name, serviceType, resolved);
        }
    }

    private static async Task<List<ProvisioningSpecFile>> ReadSpecificationsAsync(string root, List<ProvisioningError> errors, CancellationToken cancellationToken)
    {
        var specs = new List<ProvisioningSpecFile>();
        var directory = Path.Combine(root, "specs");
        if (!Directory.Exists(directory))
        {
            return specs;
        }

        // Ordinal order, so runs are repeatable whatever the file system returns.
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(file => SpecExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                var mapping = YamlJson.LoadMapping(content, relativePath);
                var keys = mapping.Children.Keys.OfType<SharpYaml.Serialization.YamlScalarNode>().Select(key => key.Value).ToHashSet(StringComparer.Ordinal);
                SpecificationKind? kind = keys.Contains("asyncapi") ? SpecificationKind.AsyncApi
                    : keys.Contains("openapi") || keys.Contains("swagger") ? SpecificationKind.OpenApi
                    : null;

                if (kind is null)
                {
                    errors.Add(new ProvisioningError(relativePath, "Not an OpenAPI or AsyncAPI document: no openapi, swagger or asyncapi key at its root."));
                    continue;
                }

                specs.Add(new ProvisioningSpecFile(relativePath, kind.Value, content));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add(new ProvisioningError(relativePath, ex.Message));
            }
        }

        return specs;
    }

    private async Task<ProvisioningManifest> ReadManifestAsync(string root, List<ProvisioningError> errors, CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, ManifestFileName);
        if (!File.Exists(path))
        {
            return ProvisioningManifest.Empty;
        }

        JsonNode? json;
        try
        {
            json = YamlJson.ToJson(YamlJson.LoadMapping(await File.ReadAllTextAsync(path, cancellationToken), ManifestFileName));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add(new ProvisioningError(ManifestFileName, ex.Message));
            return ProvisioningManifest.Empty;
        }

        var evaluation = ManifestSchema.Value.Evaluate(json, new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical });
        if (!evaluation.IsValid)
        {
            foreach (var (location, message) in FailuresOf(evaluation).Distinct())
            {
                errors.Add(new ProvisioningError($"{ManifestFileName}{location}", message));
            }

            if (!errors.Any(error => error.Source.StartsWith(ManifestFileName, StringComparison.Ordinal)))
            {
                errors.Add(new ProvisioningError(ManifestFileName, "Doesn't match provisioning-manifest.v1."));
            }

            return ProvisioningManifest.Empty;
        }

        var document = json.Deserialize<ManifestDocument>(ManifestJson)!;
        var connections = new List<ManifestConnection>();
        foreach (var connection in document.Connections ?? [])
        {
            var value = connection.Value ?? configuration[connection.ValueFrom!];
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(new ProvisioningError($"connections[{connection.Name}]", $"valueFrom \"{connection.ValueFrom}\" isn't set in the configuration."));
                continue;
            }

            connections.Add(new ManifestConnection(connection.Name, connection.Type, value));
        }

        return new ProvisioningManifest(
            connections,
            (document.Specifications ?? []).Select(spec => new ManifestSpecification(spec.Title, spec.ProviderMode, spec.DisabledOperations ?? [])).ToList(),
            (document.Publishers ?? []).Select(publisher => new ManifestPublisher(
                publisher.Name, publisher.Specification, publisher.Operation, publisher.Connection, publisher.IntervalSeconds,
                publisher.Exchange, publisher.PayloadOverride, publisher.Enabled ?? true, publisher.BrokerOptions)).ToList(),
            (document.TestScenarios ?? []).Select(scenario => new ManifestTestScenario(
                scenario.Name, scenario.Specification, scenario.Operation, scenario.Connection, scenario.Kind,
                scenario.ListenTimeoutSeconds, scenario.Exchange, scenario.PayloadOverride,
                scenario.Schedule?.Cron, scenario.Schedule?.TimeZone, scenario.BrokerOptions)).ToList(),
            (document.TestSuites ?? []).Select(suite => new ManifestTestSuite(suite.Name, suite.Scenarios, suite.RunOnStartup ?? false)).ToList());
    }

    /// <summary>
    /// The errors that actually make the manifest invalid. A subschema that passed is skipped with
    /// everything under it: a passing <c>oneOf</c> still carries its failed branches ("value is
    /// required" next to a valid <c>valueFrom</c>), which would only mislead.
    /// </summary>
    private static IEnumerable<(string Location, string Message)> FailuresOf(EvaluationResults results)
    {
        if (results.IsValid)
        {
            yield break;
        }

        foreach (var message in results.Errors?.Values ?? [])
        {
            yield return (results.InstanceLocation.ToString(), message);
        }

        foreach (var child in results.Details)
        {
            foreach (var failure in FailuresOf(child))
            {
                yield return failure;
            }
        }
    }

    private static JsonSchema LoadSchema()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(SchemaResource)
            ?? throw new InvalidOperationException($"Embedded resource {SchemaResource} is missing.");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    }

    // The manifest as written — the schema has already checked it, so these only map its shape.
    private sealed record ManifestDocument(
        int Version,
        List<ConnectionEntry>? Connections,
        List<SpecificationEntry>? Specifications,
        List<PublisherEntry>? Publishers,
        List<TestScenarioEntry>? TestScenarios,
        List<TestSuiteEntry>? TestSuites);

    private sealed record ConnectionEntry(string Name, ConnectionServiceType Type, string? Value, string? ValueFrom);

    private sealed record SpecificationEntry(string Title, bool ProviderMode, List<string>? DisabledOperations);

    private sealed record PublisherEntry(
        string Name, string Specification, string Operation, string Connection, int IntervalSeconds,
        string? Exchange, string? PayloadOverride, bool? Enabled, Dictionary<string, string?>? BrokerOptions);

    private sealed record TestScenarioEntry(
        string Name, string Specification, string Operation, string Connection, TestScenarioKind? Kind,
        int? ListenTimeoutSeconds, string? Exchange, string? PayloadOverride, ScheduleEntry? Schedule,
        Dictionary<string, string?>? BrokerOptions);

    private sealed record ScheduleEntry(string Cron, string? TimeZone);

    private sealed record TestSuiteEntry(string Name, List<string> Scenarios, bool? RunOnStartup);
}
