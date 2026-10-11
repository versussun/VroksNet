extern alias protobufnet;

using System.Text.RegularExpressions;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Domain.TestScenarios;
using ParserFileSystem = protobufnet::Google.Protobuf.Reflection.IFileSystem;
using ParserSet = protobufnet::Google.Protobuf.Reflection.FileDescriptorSet;

namespace VroksNet.Infrastructure.Specifications.Proto;

/// <summary>
/// Parses .proto text in managed code with protobuf-net.Reflection (ADR 0004), then loads the result
/// into Google.Protobuf's runtime descriptors for everything after import.
/// <list type="bullet">
/// <item>Imports resolve among the given files, in memory; the well-known types
/// (<c>google/protobuf/*.proto</c>) come with the library. A file whose path lost or gained folders
/// on the way — a browser uploads bare file names, a zipped folder adds its own name — is moved to
/// the path an import names, when it's the only file that fits (<see cref="AlignWithImports"/>).</item>
/// <item>Every given file that declares a service becomes a specification, with its own descriptor
/// set (the file and its imports, each after the files it imports).</item>
/// <item>Proto2 groups and extensions, and editions, are refused: proto3 JSON has no form for them.</item>
/// <item>Examples are generated from the response message's schema (<see cref="ProtoJsonSchema"/>):
/// a .proto file has none of its own.</item>
/// </list>
/// </summary>
public sealed partial class ProtoSpecificationParser : IProtoSpecificationParser
{
    private const string WellKnownTypesFolder = "google/protobuf/";

    public ParsedProtoUpload Parse(IReadOnlyDictionary<string, string> files)
    {
        var sources = AlignWithImports(files.ToDictionary(file => NormalizePath(file.Key), file => file.Value, StringComparer.Ordinal));
        if (sources.Count == 0)
        {
            throw Failure(null, "No .proto files were given.");
        }

        var parsed = new ParserSet { FileSystem = new InMemoryFiles(sources) };
        // An empty import path makes imports resolve through FileSystem, by their own path.
        parsed.AddImportPath(string.Empty);
        foreach (var path in sources.Keys.Order(StringComparer.Ordinal))
        {
            parsed.Add(path, includeInOutput: true);
        }

        parsed.Process();
        var errors = parsed.GetErrors()
            .Where(error => error.IsError)
            .Select(error => new SpecificationParseError(error.File, error.LineNumber, error.ColumnNumber, error.Message))
            .ToList();
        if (errors.Count > 0)
        {
            throw new SpecificationParseException(errors);
        }

        var descriptors = ToGoogleDescriptors(parsed).File.ToDictionary(file => file.Name, StringComparer.Ordinal);
        var unsupported = sources.Keys.Order(StringComparer.Ordinal).SelectMany(path => Unsupported(descriptors[path])).ToList();
        if (unsupported.Count > 0)
        {
            throw new SpecificationParseException(unsupported);
        }

        var specifications = new List<ParsedProtoSpecification>();
        var importOnly = new List<string>();
        foreach (var path in sources.Keys.Order(StringComparer.Ordinal))
        {
            if (descriptors[path].Service.Count == 0)
            {
                importOnly.Add(path);
                continue;
            }

            specifications.Add(Specification(path, sources, descriptors));
        }

        if (specifications.Count == 0)
        {
            throw Failure(null, "No file declares a service, so there's nothing to mock. Include the file with the service, along with the files it imports.");
        }

        return new ParsedProtoUpload(specifications, importOnly);
    }

    private static ParsedProtoSpecification Specification(string path, Dictionary<string, string> sources, Dictionary<string, FileDescriptorProto> descriptors)
    {
        var ordered = DependenciesFirst(path, descriptors);
        var set = new FileDescriptorSet();
        set.File.AddRange(ordered);
        FileDescriptor runtime;
        try
        {
            runtime = FileDescriptor.BuildFromByteStrings(ordered.Select(file => file.ToByteString())).Last();
        }
        catch (DescriptorValidationException ex)
        {
            // Google.Protobuf checks some things protobuf-net doesn't; its message names the symbol.
            throw Failure(path, ex.Message);
        }

        var operations = runtime.Services
            .SelectMany(service => service.Methods)
            .Select(method =>
            {
                var responseSchema = ProtoJsonSchema.For(method.OutputType);
                return new ParsedOperation(
                    $"{OperationCompatibility.RpcPrefix}/{method.Service.FullName}/{method.Name}",
                    SchemaExampleGenerator.Generate(responseSchema),
                    RequestSchemaJson: ProtoJsonSchema.For(method.InputType),
                    ResponseSchemaJson: responseSchema,
                    ExampleIsGenerated: true,
                    RpcKind: (method.IsClientStreaming, method.IsServerStreaming) switch
                    {
                        (false, false) => RpcKind.Unary,
                        (false, true) => RpcKind.ServerStreaming,
                        (true, false) => RpcKind.ClientStreaming,
                        (true, true) => RpcKind.BidiStreaming
                    });
            })
            .ToList();

        var imports = ordered
            .Select(file => file.Name)
            .Where(name => name != path && sources.ContainsKey(name))
            .ToDictionary(name => name, name => sources[name], StringComparer.Ordinal);

        return new ParsedProtoSpecification(
            ProtoTitle.Of(runtime.Package, runtime.Services.Select(service => service.Name), path),
            path,
            sources[path],
            imports,
            set.ToByteArray(),
            operations);
    }

    /// <summary>
    /// Moves a file to the path an import names when it isn't there but exactly one file fits it:
    /// <c>money.proto</c> for <c>import "common/money.proto"</c> (folders lost: a browser sends bare
    /// file names), or <c>protos/common/money.proto</c> for it (folders gained: a zipped directory). A
    /// file that an import already names exactly stays; a file two imports would claim, or an import
    /// two files would fit, is left for the parser to report as missing.
    /// </summary>
    private static Dictionary<string, string> AlignWithImports(Dictionary<string, string> sources)
    {
        var imports = sources.Values
            .SelectMany(text => ImportPattern().Matches(text).Select(match => NormalizePath(match.Groups[1].Value)))
            .ToHashSet(StringComparer.Ordinal);
        var claims = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var import in imports.Where(import => !sources.ContainsKey(import) && !import.StartsWith(WellKnownTypesFolder, StringComparison.Ordinal)))
        {
            var candidates = sources.Keys
                .Where(path => !imports.Contains(path) && (path.EndsWith("/" + import, StringComparison.Ordinal) || import.EndsWith("/" + path, StringComparison.Ordinal)))
                .ToList();
            if (candidates is [var only])
            {
                (claims.TryGetValue(only, out var wanted) ? wanted : claims[only] = new HashSet<string>(StringComparer.Ordinal)).Add(import);
            }
        }

        var aligned = new Dictionary<string, string>(sources, StringComparer.Ordinal);
        foreach (var (path, wanted) in claims.Where(claim => claim.Value.Count == 1))
        {
            aligned.Remove(path);
            aligned[wanted.Single()] = sources[path];
        }

        return aligned;
    }

    /// <summary>An <c>import "…";</c> statement (also <c>import public</c>/<c>import weak</c>), at the start of a line.</summary>
    [GeneratedRegex("""^\s*import\s+(?:public\s+|weak\s+)?"([^"]+)"\s*;""", RegexOptions.Multiline)]
    private static partial Regex ImportPattern();

    /// <summary>The file and everything it imports, each after the files it imports — the order <see cref="FileDescriptor.BuildFromByteStrings"/> needs.</summary>
    private static List<FileDescriptorProto> DependenciesFirst(string path, Dictionary<string, FileDescriptorProto> descriptors)
    {
        var ordered = new List<FileDescriptorProto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Visit(descriptors[path]);
        return ordered;

        void Visit(FileDescriptorProto file)
        {
            if (!seen.Add(file.Name))
            {
                return;
            }

            foreach (var dependency in file.Dependency)
            {
                Visit(descriptors[dependency]);
            }

            ordered.Add(file);
        }
    }

    /// <summary>protobuf-net's descriptor types are its own; their wire form is the same, so it carries them over to Google.Protobuf's.</summary>
    private static FileDescriptorSet ToGoogleDescriptors(ParserSet parsed)
    {
        using var buffer = new MemoryStream();
        protobufnet::ProtoBuf.Serializer.Serialize(buffer, parsed);
        return FileDescriptorSet.Parser.ParseFrom(buffer.ToArray());
    }

    private static IEnumerable<SpecificationParseError> Unsupported(FileDescriptorProto file)
    {
        if (file.Syntax == "editions")
        {
            yield return new SpecificationParseError(file.Name, null, null, "Protobuf editions aren't supported; use syntax = \"proto3\" or \"proto2\".");
            yield break;
        }

        if (file.Extension.Count > 0)
        {
            yield return new SpecificationParseError(file.Name, null, null, "Extensions (\"extend\") aren't supported: proto3 JSON has no form for them.");
        }

        foreach (var message in MessagesOf(file.MessageType))
        {
            if (message.Extension.Count > 0)
            {
                yield return new SpecificationParseError(file.Name, null, null, $"Message {message.Name} declares extensions (\"extend\"), which aren't supported: proto3 JSON has no form for them.");
            }

            foreach (var field in message.Field.Where(field => field.Type == FieldDescriptorProto.Types.Type.Group))
            {
                yield return new SpecificationParseError(file.Name, null, null, $"Field {message.Name}.{field.Name} is a group, which isn't supported; declare it as a message field instead.");
            }
        }
    }

    private static IEnumerable<DescriptorProto> MessagesOf(IEnumerable<DescriptorProto> messages)
        => messages.SelectMany(message => MessagesOf(message.NestedType).Prepend(message));

    private static SpecificationParseException Failure(string? file, string message)
        => new([new SpecificationParseError(file, null, null, message)]);

    /// <summary>"./a\b.proto" → "a/b.proto": the form imports name files by.</summary>
    private static string NormalizePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimStart('/');
    }

    /// <summary>Serves the given files to protobuf-net's import resolution, by path.</summary>
    private sealed class InMemoryFiles(Dictionary<string, string> files) : ParserFileSystem
    {
        public bool Exists(string path) => files.ContainsKey(NormalizePath(path));

        public TextReader? OpenText(string path) => files.TryGetValue(NormalizePath(path), out var text) ? new StringReader(text) : null;
    }
}
