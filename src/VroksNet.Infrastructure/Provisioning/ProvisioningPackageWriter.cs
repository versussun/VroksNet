using System.IO.Compression;
using System.Text;
using System.Text.Json;
using VroksNet.Application.Abstractions;
using VroksNet.Application.Provisioning;
using VroksNet.Application.Provisioning.ExportProvisioning;

namespace VroksNet.Infrastructure.Provisioning;

/// <summary>
/// Zips an export as a provisioning directory: <c>specs/&lt;file&gt;</c> plus <c>vroksnet.yaml</c>
/// (docs/schemas/provisioning-manifest.v1.schema.json). The YAML is written by hand: every string
/// is a JSON-style double-quoted scalar, which YAML reads the same way, so no value — a payload
/// with quotes and newlines, a cron expression, "yes" — can change meaning or break the file.
/// </summary>
public sealed class ProvisioningPackageWriter : IProvisioningPackageWriter
{
    public const string ManifestFileName = "vroksnet.yaml";

    public byte[] Write(ProvisioningExport export)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var spec in export.Specifications)
            {
                Add(zip, $"specs/{spec.FileName}", spec.Content);
            }

            Add(zip, ManifestFileName, ManifestOf(export));
        }

        return buffer.ToArray();
    }

    public static string ManifestOf(ProvisioningExport export)
    {
        var yaml = new StringBuilder();
        yaml.AppendLine("# yaml-language-server: $schema=https://raw.githubusercontent.com/versussun/VroksNet/master/docs/schemas/provisioning-manifest.v1.schema.json");
        yaml.AppendLine($"# Exported from VroksNet {export.AppVersion} at {export.ExportedAt:u}. Mount this directory at /app/provisioning.");
        var fromConfiguration = export.Connections.Where(connection => connection.ValueFrom is not null).ToList();
        if (fromConfiguration.Count > 0)
        {
            yaml.AppendLine("#");
            yaml.AppendLine("# Connection values aren't exported. Set these when starting the container:");
            foreach (var connection in fromConfiguration)
            {
                yaml.AppendLine($"#   {connection.ValueFrom!.Replace(":", "__", StringComparison.Ordinal)}   ({connection.Type} connection \"{connection.Name}\")");
            }
        }

        if (export.Notes.Count > 0)
        {
            yaml.AppendLine("#");
            yaml.AppendLine("# Notes:");
            foreach (var note in export.Notes)
            {
                yaml.AppendLine($"#   - {note}");
            }
        }

        yaml.AppendLine("version: 1");

        Section(yaml, "connections", export.Connections, (item, connection) =>
        {
            item.Add("name", Quote(connection.Name));
            item.Add("type", connection.Type.ToString());
            if (connection.ValueFrom is { } valueFrom)
            {
                item.Add("valueFrom", Quote(valueFrom));
            }
            else
            {
                item.Add("value", Quote(connection.Value ?? string.Empty));
            }
        });

        Section(yaml, "specifications", export.SpecificationSettings, (item, settings) =>
        {
            item.Add("title", Quote(settings.Title));
            item.Add("providerMode", Bool(settings.ProviderMode));
            if (settings.DisabledOperations.Count > 0)
            {
                item.Add("disabledOperations", List(settings.DisabledOperations));
            }
        });

        Section(yaml, "publishers", export.Publishers, (item, publisher) =>
        {
            item.Add("name", Quote(publisher.Name));
            item.Add("specification", Quote(publisher.Specification));
            item.Add("operation", Quote(publisher.Operation));
            item.Add("connection", Quote(publisher.Connection));
            item.Add("intervalSeconds", publisher.IntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            item.AddIfSet("exchange", publisher.Exchange);
            AddBrokerOptions(item, publisher.BrokerOptions);
            item.AddIfSet("payloadOverride", publisher.PayloadOverride);
            item.Add("enabled", Bool(publisher.Enabled));
        });

        Section(yaml, "testScenarios", export.TestScenarios, (item, scenario) =>
        {
            item.Add("name", Quote(scenario.Name));
            item.Add("specification", Quote(scenario.Specification));
            item.Add("operation", Quote(scenario.Operation));
            item.Add("connection", Quote(scenario.Connection));
            if (scenario.Kind is { } kind)
            {
                item.Add("kind", kind.ToString());
            }

            if (scenario.ListenTimeoutSeconds is { } timeout)
            {
                item.Add("listenTimeoutSeconds", timeout.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            item.AddIfSet("exchange", scenario.Exchange);
            AddBrokerOptions(item, scenario.BrokerOptions);
            item.AddIfSet("payloadOverride", scenario.PayloadOverride);
            if (scenario.Schedule is { } cron)
            {
                item.Add("schedule", scenario.ScheduleTimeZone is { } zone
                    ? $"{{ cron: {Quote(cron)}, timeZone: {Quote(zone)} }}"
                    : $"{{ cron: {Quote(cron)} }}");
            }
        });

        Section(yaml, "testSuites", export.TestSuites, (item, suite) =>
        {
            item.Add("name", Quote(suite.Name));
            item.Add("scenarios", List(suite.Scenarios));
            item.Add("runOnStartup", Bool(suite.RunOnStartup));
        });

        return yaml.ToString();
    }

    /// <summary>A flow map of quoted keys and values, <c>{ "qos": "1" }</c>; nothing when there are no options.</summary>
    private static void AddBrokerOptions(Item item, IReadOnlyDictionary<string, string?>? options)
    {
        var set = options?.Where(option => option.Value is not null).ToList() ?? [];
        if (set.Count > 0)
        {
            item.Add("brokerOptions", $"{{ {string.Join(", ", set.Select(option => $"{Quote(option.Key)}: {Quote(option.Value!)}"))} }}");
        }
    }

    private static void Section<T>(StringBuilder yaml, string key, IReadOnlyList<T> items, Action<Item, T> fill)
    {
        if (items.Count == 0)
        {
            return;
        }

        yaml.AppendLine();
        yaml.AppendLine($"{key}:");
        foreach (var value in items)
        {
            var item = new Item();
            fill(item, value);
            for (var i = 0; i < item.Fields.Count; i++)
            {
                yaml.AppendLine($"{(i == 0 ? "  - " : "    ")}{item.Fields[i].Key}: {item.Fields[i].Value}");
            }
        }
    }

    private static void Add(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    /// <summary>A double-quoted scalar; JSON's escaping is valid YAML's.</summary>
    private static string Quote(string value) => JsonSerializer.Serialize(value);

    private static string Bool(bool value) => value ? "true" : "false";

    private static string List(IEnumerable<string> values) => $"[{string.Join(", ", values.Select(Quote))}]";

    /// <summary>One sequence entry's fields, in order.</summary>
    private sealed class Item
    {
        public List<KeyValuePair<string, string>> Fields { get; } = [];

        public void Add(string key, string value) => Fields.Add(new(key, value));

        public void AddIfSet(string key, string? value)
        {
            if (value is not null)
            {
                Add(key, Quote(value));
            }
        }
    }
}
