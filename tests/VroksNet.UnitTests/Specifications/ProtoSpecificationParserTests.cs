using Google.Protobuf;
using Google.Protobuf.Reflection;
using VroksNet.Application.Abstractions;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Infrastructure.SchemaValidation;
using VroksNet.Infrastructure.Specifications.Proto;

namespace VroksNet.UnitTests.Specifications;

/// <summary>
/// <see cref="ProtoSpecificationParser"/> and the schemas <see cref="ProtoJsonSchema"/> builds (ADR 0004):
/// the gRPC samples parse into the specifications docs/samples promises, each schema accepts what the
/// proto3 JSON mapping allows and refuses the rest, and broken input fails with its file and line.
/// </summary>
public sealed class ProtoSpecificationParserTests
{
    private readonly ProtoSpecificationParser _parser = new();
    private readonly SchemaValidator _validator = new();

    [Fact]
    public void Samples_EveryFileWithAServiceIsASpecification_AndTheRestAreImports()
    {
        var upload = _parser.Parse(Samples());

        Assert.Equal(["demo.greeter.v1 (Greeter)", "shop.orders.v1 (Orders, Payments)"], upload.Specifications.Select(spec => spec.Title));
        Assert.Equal(["common/money.proto"], upload.ImportOnlyFiles);

        var shop = upload.Specifications[1];
        Assert.Equal("shop-orders.proto", shop.Path);
        // Its own import, but not the well-known types, which come with VroksNet.
        Assert.Equal(["common/money.proto"], shop.Imports.Keys);
        Assert.Equal(
            ["RPC /shop.orders.v1.Orders/GetOrder", "RPC /shop.orders.v1.Orders/ListOrders", "RPC /shop.orders.v1.Orders/WatchOrders", "RPC /shop.orders.v1.Payments/Pay"],
            shop.Operations.Select(operation => operation.OperationKey));
        Assert.Equal(
            [RpcKind.Unary, RpcKind.Unary, RpcKind.ServerStreaming, RpcKind.Unary],
            shop.Operations.Select(operation => operation.RpcKind!.Value));
    }

    [Fact]
    public void Samples_EachDescriptorSetLoadsWithoutTheProtoText()
    {
        foreach (var spec in _parser.Parse(Samples()).Specifications)
        {
            var set = FileDescriptorSet.Parser.ParseFrom(spec.DescriptorSet);
            var files = FileDescriptor.BuildFromByteStrings(set.File.Select(file => file.ToByteString()));

            Assert.Equal(spec.Path, files[^1].Name);
            Assert.NotEmpty(files[^1].Services);
        }
    }

    [Fact]
    public void Samples_EveryOperationHasAGeneratedExampleThatMatchesItsResponseSchema()
    {
        foreach (var operation in _parser.Parse(Samples()).Specifications.SelectMany(spec => spec.Operations))
        {
            Assert.True(operation.ExampleIsGenerated);
            Assert.NotNull(operation.ExampleJson);
            Assert.NotNull(operation.RequestSchemaJson);
            var result = _validator.Validate(operation.ResponseSchemaJson!, operation.ExampleJson);
            Assert.True(result.IsValid, $"{operation.OperationKey}: {string.Join("; ", result.Errors)}");
        }
    }

    [Fact]
    public void Schema_FollowsTheProto3JsonMapping()
    {
        var getOrder = Operation("RPC /shop.orders.v1.Orders/GetOrder");

        // 64-bit integers as a number or a decimal string; enums by name or number; JSON field names.
        AssertValid(getOrder.ResponseSchemaJson, """{ "id": "1", "status": "PAID", "total": { "currencyCode": "EUR", "units": "12", "nanos": 500000000 }, "version": 3 }""");
        AssertValid(getOrder.ResponseSchemaJson, """{ "status": 2, "total": { "units": 12 }, "labels": { "gift": "yes" }, "createdAt": "2026-10-11T10:00:00Z", "note": "leave at the door" }""");
        AssertInvalid(getOrder.ResponseSchemaJson, """{ "status": "LOST" }""");
        AssertInvalid(getOrder.ResponseSchemaJson, """{ "total": { "units": "twelve" } }""");
        // Unknown properties, and the .proto field names in place of the JSON ones, aren't the canonical form.
        AssertInvalid(getOrder.ResponseSchemaJson, """{ "customer_id": "c1" }""");
        AssertInvalid(getOrder.ResponseSchemaJson, """{ "surprise": true }""");
    }

    [Fact]
    public void Schema_AOneofAllowsAtMostOneOfItsFields_AndTheExampleSetsTheFirst()
    {
        var pay = Operation("RPC /shop.orders.v1.Payments/Pay");

        AssertValid(pay.RequestSchemaJson, """{ "orderId": "o1" }""");
        AssertValid(pay.RequestSchemaJson, """{ "orderId": "o1", "invoiceNumber": "INV-1" }""");
        AssertInvalid(pay.RequestSchemaJson, """{ "orderId": "o1", "cardToken": "tok", "invoiceNumber": "INV-1" }""");

        var example = SchemaExampleGeneratorFor(pay.RequestSchemaJson!);
        Assert.Contains("\"cardToken\"", example);
        Assert.DoesNotContain("\"invoiceNumber\"", example);
    }

    [Fact]
    public void Example_OfEmpty_IsAnEmptyObject()
        => Assert.Equal("{}", Operation("RPC /shop.orders.v1.Payments/Pay").ExampleJson);

    [Fact]
    public void Schema_ARecursiveMessageRefersToItself()
    {
        var spec = Assert.Single(_parser.Parse(new Dictionary<string, string>
        {
            ["tree.proto"] = """
                syntax = "proto3";
                package tree.v1;
                service Trees { rpc Get (Node) returns (Node); }
                message Node { string name = 1; repeated Node children = 2; }
                """
        }).Specifications);
        var operation = Assert.Single(spec.Operations);

        Assert.Contains("\"#/$defs/tree.v1.Node\"", operation.ResponseSchemaJson);
        AssertValid(operation.ResponseSchemaJson, """{ "name": "root", "children": [{ "name": "leaf", "children": [] }] }""");
        Assert.NotNull(operation.ExampleJson);
    }

    [Fact]
    public void Title_WithoutAPackage_IsTheFileName()
    {
        var spec = Assert.Single(_parser.Parse(new Dictionary<string, string>
        {
            ["./protos\\ping.proto"] = """syntax = "proto3"; service Ping { rpc Check (Empty) returns (Empty); } message Empty {}"""
        }).Specifications);

        Assert.Equal("ping (Ping)", spec.Title);
        Assert.Equal("protos/ping.proto", spec.Path);
        Assert.Equal("RPC /Ping/Check", Assert.Single(spec.Operations).OperationKey);
    }

    /// <summary>A browser uploads bare file names: money.proto still serves import "common/money.proto".</summary>
    [Fact]
    public void Parse_FilesThatLostTheirFolders_AreMovedToThePathTheirImportNames()
    {
        var flat = Samples().ToDictionary(file => file.Key.Split('/')[^1], file => file.Value);

        var upload = _parser.Parse(flat);

        Assert.Equal(["common/money.proto"], upload.ImportOnlyFiles);
        Assert.Equal(["common/money.proto"], upload.Specifications.Single(spec => spec.Path == "shop-orders.proto").Imports.Keys);
    }

    /// <summary>A zipped directory adds its own name: protos/common/money.proto serves import "common/money.proto".</summary>
    [Fact]
    public void Parse_FilesUnderAnExtraFolder_AreMovedToThePathTheirImportNames()
    {
        var nested = Samples().ToDictionary(file => "protos/" + file.Key, file => file.Value);

        var upload = _parser.Parse(nested);

        Assert.Equal(["common/money.proto"], upload.ImportOnlyFiles);
        Assert.Contains(upload.Specifications, spec => spec.Path == "protos/shop-orders.proto");
    }

    /// <summary>protoc's convention: files in package folders, imported with the folder — nothing to move.</summary>
    [Fact]
    public void Parse_AFolderThatImportsNameToo_IsKept()
    {
        var upload = _parser.Parse(new Dictionary<string, string>
        {
            ["acme/orders.proto"] = """syntax = "proto3"; package acme; import "acme/common.proto"; service Orders { rpc Get (Id) returns (Id); }""",
            ["acme/common.proto"] = """syntax = "proto3"; package acme; message Id { string value = 1; }"""
        });

        Assert.Equal(["acme/common.proto"], upload.ImportOnlyFiles);
        Assert.Equal("acme/orders.proto", Assert.Single(upload.Specifications).Path);
    }

    [Fact]
    public void Parse_AnImportTwoFilesCouldServe_IsReportedMissing()
    {
        var error = Assert.Throws<SpecificationParseException>(() => _parser.Parse(new Dictionary<string, string>
        {
            ["orders.proto"] = """syntax = "proto3"; import "common/money.proto"; service Orders { rpc Get (Money) returns (Money); }""",
            ["a/money.proto"] = """syntax = "proto3"; message Money { int64 units = 1; }""",
            ["b/money.proto"] = """syntax = "proto3"; message Money { int64 units = 1; }"""
        }));

        Assert.Contains(error.Errors, item => item.File == "orders.proto" && item.Message.Contains("common/money.proto"));
    }

    [Fact]
    public void Parse_AMissingImport_FailsWithItsFileAndLine()
    {
        var error = Assert.Throws<SpecificationParseException>(() => _parser.Parse(new Dictionary<string, string>
        {
            ["orders.proto"] = """
                syntax = "proto3";
                import "common/money.proto";
                service Orders { rpc Get (Order) returns (Order); }
                message Order { string id = 1; }
                """
        }));

        var missing = Assert.Single(error.Errors, item => item.Message.Contains("common/money.proto"));
        Assert.Equal(("orders.proto", 2), (missing.File, missing.Line));
        Assert.Contains("orders.proto(2,", error.Message);
    }

    [Fact]
    public void Parse_AnUnknownType_FailsWithItsFileAndLine()
    {
        var error = Assert.Throws<SpecificationParseException>(() => _parser.Parse(new Dictionary<string, string>
        {
            ["bad.proto"] = "syntax = \"proto3\";\nmessage A { strin name = 1; }\nservice S { rpc X (A) returns (A); }"
        }));

        var unknown = Assert.Single(error.Errors);
        Assert.Equal(("bad.proto", 2), (unknown.File, unknown.Line));
        Assert.Contains("strin", unknown.Message);
    }

    [Fact]
    public void Parse_NoFileDeclaresAService_Fails()
    {
        var error = Assert.Throws<SpecificationParseException>(() => _parser.Parse(new Dictionary<string, string>
        {
            ["money.proto"] = """syntax = "proto3"; message Money { int64 units = 1; }"""
        }));

        Assert.Contains("No file declares a service", error.Message);
    }

    [Theory]
    [InlineData("""
        syntax = "proto2";
        service S { rpc X (A) returns (A); }
        message A { optional group Item = 1 { optional string name = 2; } }
        """, "is a group")]
    [InlineData("""
        syntax = "proto2";
        service S { rpc X (A) returns (A); }
        message A { extensions 100 to 199; }
        extend A { optional string tag = 100; }
        """, "Extensions")]
    public void Parse_Proto2GroupsAndExtensions_AreRefused(string proto, string expected)
    {
        var error = Assert.Throws<SpecificationParseException>(() => _parser.Parse(new Dictionary<string, string> { ["legacy.proto"] = proto }));

        Assert.Contains(error.Errors, item => item.File == "legacy.proto" && item.Message.Contains(expected));
    }

    [Fact]
    public void Parse_NoFiles_Fails()
        => Assert.Throws<SpecificationParseException>(() => _parser.Parse(new Dictionary<string, string>()));

    private ParsedOperation Operation(string key)
        => _parser.Parse(Samples()).Specifications.SelectMany(spec => spec.Operations).Single(operation => operation.OperationKey == key);

    private static string SchemaExampleGeneratorFor(string schemaJson)
        => Infrastructure.Specifications.SchemaExampleGenerator.Generate(schemaJson)!;

    private void AssertValid(string? schemaJson, string json)
    {
        var result = _validator.Validate(schemaJson!, json);
        Assert.True(result.IsValid, $"{json}: {string.Join("; ", result.Errors)}");
    }

    private void AssertInvalid(string? schemaJson, string json)
        => Assert.False(_validator.Validate(schemaJson!, json).IsValid, $"{json} should not match.");

    /// <summary>docs/samples/grpc, keyed by path under that folder — as provisioning or a .zip of the folder hands them over.</summary>
    internal static Dictionary<string, string> Samples()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Samples", "grpc");
        return Directory.EnumerateFiles(root, "*.proto", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(root, file).Replace('\\', '/'), File.ReadAllText);
    }
}
