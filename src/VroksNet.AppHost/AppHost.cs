var builder = DistributedApplication.CreateBuilder(args);

// Which broker containers to start: "Brokers" is a comma-separated list of the resource names
// below, all of them when it isn't set — so local development always gets every broker. The
// integration tests set it to start only what a test collection needs (step R6 of
// docs/broker-adapters-plan.md); "--Brokers=" starts none.
string[] knownBrokers = ["rabbitmq", "nats", "kafka", "mqtt", "redis", "servicebus", "localstack"];
var brokers = builder.Configuration["Brokers"] is { } configured
    ? configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase)
    : knownBrokers.ToHashSet(StringComparer.OrdinalIgnoreCase);
if (brokers.Except(knownBrokers, StringComparer.OrdinalIgnoreCase).ToList() is { Count: > 0 } unknown)
{
    throw new InvalidOperationException($"Unknown broker(s) in \"Brokers\": {string.Join(", ", unknown)}. Known: {string.Join(", ", knownBrokers)}.");
}

// Pinned to a fixed port: under AppHost orchestration, Aspire assigns each project resource a
// random port every run rather than honoring its launchSettings.json applicationUrl — confirmed
// by inspecting actual listening ports across several runs, both with and without an explicit
// launch profile. VroksNet.Web (a standalone Blazor WASM app, see below) has no way to discover
// a random port at runtime, so this one has to be stable. Must match the port hardcoded in
// VroksNet.Web/wwwroot/appsettings.Development.json ("ApiService:BaseAddress").
var apiService = builder.AddProject<Projects.VroksNet_ApiService>("apiservice")
    .WithHttpsEndpoint(port: 7352, name: "https")
    // Provider mode's port (see ProviderPortSetup in ApiService): the mock at real spec paths.
    // Pinned too, so a service under test can be pointed at a stable http://localhost:7353.
    .WithHttpEndpoint(port: 7353, name: "provider", env: "Provider__Port")
    // What the Admin UI tells users to point a service at (Provider__Port is the process-side
    // target port behind Aspire's proxy, not this one).
    .WithEnvironment("Provider__PublicUrl", "http://localhost:7353")
    // CORS on the provider port is off by default; to let a browser front end call the mock, list
    // its origins (or "*"):
    // .WithEnvironment("Provider__CorsOrigins", "http://localhost:5173")
    .WithHttpHealthCheck("/health");

if (brokers.Contains("rabbitmq"))
{
    var rabbitmq = builder.AddRabbitMQ("rabbitmq");
    apiService.WithReference(rabbitmq).WaitFor(rabbitmq);
}

if (brokers.Contains("nats"))
{
    var nats = builder.AddNats("nats");
    apiService.WithReference(nats).WaitFor(nats);
}

if (brokers.Contains("kafka"))
{
    var kafka = builder.AddKafka("kafka");
    kafka.WithEnvironment("KAFKA_AUTO_CREATE_TOPICS_ENABLE", "true");
    apiService.WithReference(kafka).WaitFor(kafka);
}

if (brokers.Contains("mqtt"))
{
    // No Aspire hosting integration for MQTT: a plain Mosquitto container, anonymous, on its
    // "mqtt" endpoint. apiservice only waits for it — it reaches MQTT through user Connections.
    var mqtt = builder.AddContainer("mqtt", "eclipse-mosquitto", "2")
        .WithArgs("mosquitto", "-c", "/mosquitto-no-auth.conf")
        .WithEndpoint(targetPort: 1883, name: "mqtt", scheme: "tcp");
    apiService.WaitFor(mqtt);
}

if (brokers.Contains("redis"))
{
    // apiservice only waits for it: it reaches Redis through user Connections, whose value is the
    // connection string WithReference would hand out (host:port,password=…).
    var redis = builder.AddRedis("redis");
    apiService.WaitFor(redis);
}

if (brokers.Contains("servicebus"))
{
    // The Service Bus emulator (plus the SQL Server container it needs). It only has the entities
    // declared here, which match docs/samples/warehouse-servicebus-asyncapi.yaml and the
    // integration tests: a queue (Send only) and a topic with a subscription named "vroksnet" for
    // Listen. apiservice only waits for it: it reaches Service Bus through user Connections, whose
    // value is the connection string WithReference would hand out (Endpoint=sb://…;…;UseDevelopmentEmulator=true).
    var serviceBus = builder.AddAzureServiceBus("servicebus").RunAsEmulator();
    serviceBus.AddServiceBusQueue("servicebus-picking", "warehouse.picking.requested");
    serviceBus.AddServiceBusTopic("servicebus-stock", "warehouse.stock.changed")
        .AddServiceBusSubscription("servicebus-stock-vroksnet", "vroksnet");
    apiService.WaitFor(serviceBus);
}

if (brokers.Contains("localstack"))
{
    // AWS SQS and SNS (plus STS, which the connection check uses) emulated by LocalStack: there's
    // no Aspire connection string for AWS, so a Connection's value is built from its endpoint
    // (Region=us-east-1;AccessKeyId=test;SecretAccessKey=test;ServiceUrl=http://localhost:<port>).
    // The init script creates the entities of docs/samples/order-events-aws-asyncapi.yaml.
    var localstack = builder.AddContainer("localstack", "localstack/localstack", "4.14")
        .WithEnvironment("SERVICES", "sqs,sns,sts")
        .WithBindMount("localstack/init-aws.sh", "/etc/localstack/init/ready.d/init-aws.sh", isReadOnly: true)
        .WithHttpEndpoint(targetPort: 4566, name: "gateway")
        .WithHttpHealthCheck("/_localstack/health", endpointName: "gateway");
    apiService.WaitFor(localstack);
}

// VroksNet.Web is a standalone Blazor WebAssembly app (Microsoft.NET.Sdk.BlazorWebAssembly).
// `dotnet run` on it launches its built-in dev server for local hot reload; in Production it
// isn't run as its own process at all — VroksNet.ApiService serves its published output as
// static files instead (see VroksNet.ApiService/Program.cs), so there is no service-discovery
// reference to apiservice here — the dev server talks to ApiService's fixed dev URL configured
// in VroksNet.Web/wwwroot/appsettings.Development.json, with CORS enabled on ApiService for it.
// Web's own dev-server port is left random — ApiService's CORS policy allows any loopback
// origin in Development, so it doesn't need to be predicted.
builder.AddProject<Projects.VroksNet_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WaitFor(apiService);

await builder.Build().RunAsync();
