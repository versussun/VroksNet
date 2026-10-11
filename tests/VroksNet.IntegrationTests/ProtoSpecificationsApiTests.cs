using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using VroksNet.IntegrationTests.Fixtures;

namespace VroksNet.IntegrationTests;

/// <summary>
/// <c>POST /api/specifications/proto</c> (ADR 0004) over HTTP: several .proto files in one multipart
/// request, or one .zip of a folder, with imports resolved among them; broken input answers 400
/// naming the file and line. Each test uses its own package, so its specifications are its own.
/// </summary>
[Collection("AppHost")]
public sealed class ProtoSpecificationsApiTests(AppHostFixture fixture)
{
    [Fact]
    public async Task ImportSeveralFiles_ResolvesTheirImports_AndListsTheMethods()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var package = UniquePackage();

        using var response = await PostAsync(
            ("orders.proto", Orders(package)),
            ("common/money.proto", Money(package)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!;
        Assert.Equal("common/money.proto", Assert.Single(result["importOnlyFiles"]!.AsArray())!.GetValue<string>());
        var imported = Assert.Single(result["specifications"]!.AsArray())!;
        Assert.Equal($"{package} (Orders)", imported["title"]!.GetValue<string>());
        Assert.False(imported["replaced"]!.GetValue<bool>());

        var details = (await fixture.ApiServiceClient.GetFromJsonAsync<JsonNode>($"/api/specifications/{imported["id"]!.GetValue<Guid>()}", cancellationToken))!;
        Assert.Equal("Proto", details["kind"]!.GetValue<string>());
        var endpoints = details["endpoints"]!.AsArray();
        Assert.Equal([$"RPC /{package}.Orders/Get", $"RPC /{package}.Orders/Watch"], endpoints.Select(endpoint => endpoint!["operationKey"]!.GetValue<string>()));
        Assert.Equal(["Unary", "ServerStreaming"], endpoints.Select(endpoint => endpoint!["rpcKind"]!.GetValue<string>()));
        Assert.All(endpoints, endpoint => Assert.Equal("Rpc", endpoint!["operationShape"]!.GetValue<string>()));
        Assert.Contains("\"total\"", endpoints[0]!["exampleTemplate"]!.GetValue<string>());
    }

    [Fact]
    public async Task ImportAZipOfAFolder_DropsTheFolder_SoImportsMatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var package = UniquePackage();
        var zip = Zip(("protos/orders.proto", Orders(package)), ("protos/common/money.proto", Money(package)), ("protos/README.md", "not a proto"));

        using var response = await PostAsync("protos.zip", zip);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!;
        Assert.Equal("orders.proto", Assert.Single(result["specifications"]!.AsArray())!["path"]!.GetValue<string>());
    }

    [Fact]
    public async Task ImportWithAMissingImport_Is400_NamingTheFileAndLine()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var response = await PostAsync(("orders.proto", Orders(UniquePackage())));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var message = (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken))!["message"]!.GetValue<string>();
        Assert.Contains("orders.proto(3,", message);
        Assert.Contains("common/money.proto", message);
    }

    [Fact]
    public async Task ImportOfAFileThatIsntProto_Is400()
    {
        using var response = await PostAsync(("orders.yaml", "openapi: 3.0.3"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static string UniquePackage() => $"itest.p{Guid.NewGuid():N}";

    private static string Orders(string package) => $$"""
        syntax = "proto3";
        package {{package}};
        import "common/money.proto";
        service Orders {
          rpc Get (GetRequest) returns (Order);
          rpc Watch (GetRequest) returns (stream Order);
        }
        message GetRequest { string id = 1; }
        message Order { string id = 1; Money total = 2; }
        """;

    private static string Money(string package) => $$"""
        syntax = "proto3";
        package {{package}};
        message Money { string currency_code = 1; int64 units = 2; }
        """;

    private Task<HttpResponseMessage> PostAsync(params (string Name, string Content)[] files)
    {
        var form = new MultipartFormDataContent();
        foreach (var (name, content) in files)
        {
            form.Add(new StringContent(content, Encoding.UTF8), "files", name);
        }

        return fixture.ApiServiceClient.PostAsync("/api/specifications/proto", form, TestContext.Current.CancellationToken);
    }

    private Task<HttpResponseMessage> PostAsync(string name, byte[] content)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        var form = new MultipartFormDataContent { { file, "files", name } };
        return fixture.ApiServiceClient.PostAsync("/api/specifications/proto", form, TestContext.Current.CancellationToken);
    }

    private static byte[] Zip(params (string Path, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }
}
