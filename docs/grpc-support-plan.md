# Plan: gRPC support

**Status:** accepted — decisions in ADR 0004 (`docs/adr/0004-grpc-support.md`). **Progress:** G0 and G1 done; G2–G8 not started.
**Why:** services talk to each other over gRPC as well as REST and brokers. VroksNet should mock a gRPC service from its `.proto` file and check a real one against it, the way it already does for OpenAPI.

## What "gRPC support" means here

The same contract-testing types as for HTTP (`docs/contract-testing-plan.md`, section 2), applied to `.proto` services:

| Type | For gRPC | In this plan |
|---|---|---|
| Mock | Import a `.proto` file. Each RPC answers on a gRPC port with an example built from the response message. | G2–G4 |
| Type 1, Consumer (Send) | A Test Scenario calls the RPC on a real service through a `Grpc` connection, then checks that the response decodes as the declared message. | G5 |
| Type 3, Provider | Calls coming in to the mock are decoded and validated against the request message, and recorded in the call history. | G4 |
| Types 2/4 (async publish/listen) | Not applicable: gRPC isn't a broker. | — |

The first release is unary only. Streaming RPCs are imported and listed from the start, and the mock answers them with `UNIMPLEMENTED` until G7.

## Where the code has to change

| Place | Today | For gRPC |
|---|---|---|
| `Domain/ApiSpecifications/SpecificationKind.cs` | `OpenApi`, `AsyncApi` | add `Proto` |
| `Domain/TestScenarios/OperationCompatibility.cs` | The operation shape is read from the key: a space means HTTP (`GET /pets`), no space means AsyncAPI (`orders:send`). | A third shape, RPC. A gRPC full method name (`/shop.v1.Orders/Get`) has no space, so today it would count as AsyncAPI. |
| `Domain/Connections/ServiceTypeTraits.cs` | `bool IsHttp` | Replace it with an operation shape (`Http`/`Message`/`Rpc`) and add the `Grpc` type. `isHttp` stays in `GET /api/system/connection-types` for contract v1. |
| `Application/Abstractions/ISpecificationParser.cs` | Parses one YAML/JSON document into `ParsedOperation`s with JSON Schemas. | A `.proto` parser that produces the same shape: JSON Schemas derived from the messages, plus a generated example. |
| `Infrastructure/Brokers/*` | One `IBrokerAdapter` per connection type. | `GrpcBrokerAdapter` (test + Send, no Listen). Send needs the message descriptors, which today's `SendAsync` signature doesn't carry. |
| `ApiService/Endpoints/MockInvocationEndpoints.cs`, `ProviderPortSetup.cs` | The HTTP mock under `/mock` and on the provider port `7353` (HTTP/1.1). | A separate gRPC port (HTTP/2 without TLS, i.e. h2c) with its own framing, status in trailers and metadata. |
| `Infrastructure/Provisioning/FileProvisioningSource.cs` | `SpecExtensions = .yaml, .yml, .json` | Add `.proto`. Imports resolve relative to the `specs` folder. |
| `docs/container-contract.md` §3 | Ports `8080`, `7353` | Add a `grpc` port (additive, so still contract v1) plus the Dockerfile `EXPOSE` and the contract check script. |
| Web: Specifications, Test Scenarios, Settings | OpenAPI/AsyncAPI only | Import `.proto`, show RPC operations with their streaming kind, offer `Grpc` connections for Send. |

## Key technical decisions (proposed)

### 1. Parsing `.proto` without `protoc`

- **Library:** [`protobuf-net.Reflection`](https://www.nuget.org/packages/protobuf-net.Reflection). It parses `.proto` text into `FileDescriptorProto`s in managed code, with readable errors (file, line, column).
  - Running `protoc` at runtime would mean shipping a native binary per platform in the image. `Grpc.Tools` is build-time only.
- **Imports:** `google/protobuf/*.proto` (the well-known types) resolve without the user supplying them: the library embeds them (checked in G0).
- **Name clash:** the library declares its own `Google.Protobuf.Reflection.FileDescriptorSet`, so it's referenced through an `extern alias`, visible to the parser only.
- **Other imports:** resolved among the files given together — the provisioning `specs` folder, or the files of one UI upload (several `.proto` files or one `.zip`, in G2). An import that isn't among them fails the import with its file and line.
- **Which files become specs:** every file that declares at least one `service` becomes its own specification. Files without services are only imports. The same rule holds for provisioning and the UI.
- **What's stored:** the parsed `FileDescriptorSet` (binary, the spec plus everything it imports) on `ApiSpecification`, in a new column, in dependency order — `FileDescriptor.BuildFromByteStrings` needs imports first. Every later step works from it, so the `.proto` text is never parsed twice.

### 2. No generated code: our own JSON ↔ protobuf transcoder

- Google.Protobuf for C# has no `DynamicMessage`, unlike Java: it reads and writes only generated types. protobuf-net is code-first. So a descriptor-driven transcoder is needed. It's the largest single piece of work in this plan (G3).
  - **JSON → wire:** walk the message descriptor and write through `CodedOutputStream`.
  - **wire → JSON:** read through `CodedInputStream`.
  - **JSON format:** the canonical proto3 JSON mapping: `lowerCamelCase`/`json_name`, 64-bit integers as strings, `bytes` as base64, enums by name.
  - **Well-known types:** `Timestamp`, `Duration`, the wrappers, `Struct`/`Value`/`ListValue`, `FieldMask`, `Empty`. `Any` is supported only for types in the imported descriptor set.
  - **Also covered:** `oneof`, `map`, packed/unpacked `repeated`, proto3 `optional`, and unknown fields (kept, and reported as a warning rather than a violation, because they're how proto stays forward compatible).
  - **Not supported:** proto2 groups and extensions. The parser refuses them with a readable error.
- **Test oracle:** the unit test project generates C# for its fixture `.proto` files with `Grpc.Tools`. For each fixture, the transcoder's bytes must equal `ToByteArray()` of the generated message, and its JSON must equal `JsonFormatter`'s output. Round trips go both ways.

### 3. Reuse the JSON pipeline through JSON Schema

- At import, each message is mapped to a self-contained JSON Schema that follows the proto3 JSON mapping, with `$defs` for nested and recursive messages. The schemas go into `MockEndpoint.RequestSchema`/`ResponseSchema`, like OpenAPI's.
- Everything downstream then works unchanged:
  - `SchemaExampleGenerator` builds the example: a `.proto` file has no examples, so `ExampleIsGenerated` is always true;
  - `ResponseTemplateEngine` fills `{{request.body.*}}`, `{{uuid}}` and `{{now}}`;
  - `ISchemaValidator` validates;
  - the call history stores bodies as readable JSON.
- **Validation is two-stage.** First the bytes must decode as the message; a decode failure is a contract violation. Then the decoded canonical JSON is checked against the schema, for enum values and similar rules.
- The canonical decoder never emits unknown keys, so the schemas can use `additionalProperties: false`.

### 4. Specification title

- **Title** (the re-import key): the `package`, followed by the file's service names in parentheses, sorted by name: `shop.orders.v1 (Orders, Payments)`. Sorting keeps the title stable when services are reordered in the file.
- A file without a `package` uses its file name (without `.proto`) instead.
- **Consequence:** adding or removing a service changes the title, so the import creates a new specification instead of replacing the old one. The import result says so when an existing spec has the same package, so the user can delete the old one.

### 5. Operation key

- **Format:** `RPC /package.Service/Method`. The path part is the real gRPC path (`POST /package.Service/Method` on HTTP/2), so the key reads naturally in the UI and the call history.
- `OperationCompatibility` checks the `RPC ` prefix before the space rule. An `RPC` key fits only connections whose shape is `Rpc`, so it can't fall into the HTTP or AsyncAPI branch.
- Streaming kind (unary, server, client, bidi) is stored on `MockEndpoint`, not in the key. Changing a method's streaming kind is a re-import change, not a different operation.

### 6. The mock's gRPC port

- **Port:** a dedicated Kestrel endpoint, `Grpc__Port`, default `7354` (next to the provider port `7353`), with `Protocols = Http2`.
  - Without TLS, Kestrel accepts HTTP/2 only by prior knowledge, and only on an HTTP/2-only endpoint. That's what gRPC clients use for `http://`.
  - TLS on this port is out of scope: in production a proxy terminates it.
  - Aspire endpoint name: `grpc`.
- **No `Grpc.AspNetCore` services for mock calls.** They need generated service classes. (`Grpc.AspNetCore.Server` hosts only the reflection services of decision 8, with `IgnoreUnknownServices = true` so its fallback doesn't answer the mocked methods.) Instead, raw request handling:
  - parse the 5-byte length-prefixed frames;
  - accept `grpc-encoding: identity` and `gzip`;
  - honour `grpc-timeout`;
  - send `grpc-status`/`grpc-message` trailers.
- **Behaviour:**
  - An unknown method gets `UNIMPLEMENTED (12)`. A disabled operation gets `UNIMPLEMENTED` with a message saying it's disabled.
  - A matched unary call answers `OK` with the rendered example, encoded as the response message.
  - The request is decoded and validated against the request message, as the HTTP mock does in both modes. The answer doesn't depend on the result; a violation goes into the call history.
- **No `/mock` prefix:** gRPC clients can't put a base path in front of the method path, so every RPC answers at its real path on the gRPC port.
  - So the HTTP provider-mode flag (`ServeAtRealPath`) doesn't apply to RPC operations. Instead, at most one enabled operation may serve each full method name.
  - Enabling a second one is refused with the name of the spec that already serves it, following the `OperationOverlap` pattern.
- **Placeholders:** request metadata feeds `{{request.header.*}}`. `{{request.path.*}}` and `{{request.query.*}}` don't apply and resolve with the usual warning.

### 7. Send through a `Grpc` connection (Type 1)

- **Connection value:** a URL, `http://host:5001` (h2c) or `https://host:5001`.
- **Test:** a raw call to `grpc.health.v1.Health/Check`. Any gRPC response, `UNIMPLEMENTED` included, proves the server is reachable, matching how the Http check counts any HTTP status.
- **Send:** `Grpc.Net.Client`, called through `CallInvoker` with a `Method<byte[], byte[]>` and pass-through marshallers. That needs no generated client.
  - The adapter encodes the payload JSON into the request message and decodes the response into JSON.
  - So `IMessageSender` gains an optional per-operation context carrying the descriptor set and message type names. Only the gRPC adapter reads it; the other adapters ignore it.
- **Results:**
  - A non-`OK` status fails the run, with message `NOT_FOUND: <grpc-message>`, and isn't a contract violation, because a `.proto` declares no error shapes.
  - On `OK`, the response is validated as in decision 3.
  - `MessageSendResult` gets the gRPC status code next to the HTTP one.

### 8. Server reflection on the mock port

- Serve `grpc.reflection.v1` (and `v1alpha`, which many tools still use) from the stored descriptor sets, using `FileDescriptor.BuildFromByteStrings`.
- The package's `ReflectionServiceImpl` takes a fixed descriptor list, but specs change at runtime, so VroksNet implements both services on the package's `ServerReflectionBase` classes and reads the enabled specs per request.
- Then `grpcurl -plaintext localhost:7354 list`, Postman and Kreya discover the mocked services with no `.proto` on the client side.

## Steps

Each step is one PR with its own tests (unit, integration, and E2E where the UI changes), as the existing plans do.

### G0. ADR 0004 + spike (S) — ✅ done

- Record decisions 1–8 and the decisions at the end of this plan.
- **Spike:**
  - `protobuf-net.Reflection` on the sample files: imports, well-known types, error messages;
  - an h2c Kestrel endpoint inside the published container, called by `grpcurl`;
  - a raw `Method<byte[], byte[]>` call through `Grpc.Net.Client`.
- **Done when:** the ADR is accepted, and the spike's three checks pass (or the ADR changes to match what they found).
- **Result (2026-10-04):** all three passed, plus server reflection from runtime-built descriptors and multi-file imports from memory. ADR 0004 records the findings and the package versions.

### G1. Domain: the RPC shape (S) — ✅ done

- `SpecificationKind.Proto`.
- `OperationCompatibility`: the RPC shape (`ShapeOf`).
- `ServiceTypeTraits`: an operation shape instead of `IsHttp` (`IsHttp` stays as a computed property and in the API for contract v1). `GET /api/system/connection-types` and the spec details' endpoints carry `operationShape`, and the Web UI matches connections to operations by it.
- **Done when:** current behaviour is unchanged (existing tests green), and unit tests cover all three shapes.
- **Moved to G5:** the `Grpc` connection type. A type needs an adapter (`ServiceTypeTraitsTests`/`BrokerAdapterRegistryTests`), and a type that can do nothing shouldn't appear in the UI.

### G2. Import `.proto` (L) — after G1

- **Parser:** `ProtoSpecificationParser`.
  - Title and which files become specs: decisions 1 and 4.
  - One `ParsedOperation` per RPC, with schemas from decision 3, a generated example and the streaming kind.
  - Proto2 groups/extensions are refused with a readable error.
- **Storage:** the `FileDescriptorSet` column on `ApiSpecification` (a migration).
- **Entry points:**
  - `POST /api/specifications/proto` — multipart with one or more `.proto` files, or a single `.zip`; imports resolve among the uploaded files;
  - `.proto` in provisioning, with imports resolved from the specs folder;
  - the import picker in the UI, with multi-select and `.zip`. The result lists each spec created or replaced, and the files used only as imports.
- **Re-import:** works by title, as today.
- **Samples:** `docs/samples/greeter.proto` and `docs/samples/shop-orders.proto`. The second one uses imports, `Timestamp`, nested messages, enum, `oneof`, `map`, `repeated` and a server-streaming RPC.
- **Done when:** both samples import through the UI (as several files and as a `.zip`) and provisioning and show their RPCs with generated examples; an upload with a missing import, and a broken `.proto`, fail with the file and line.

### G3. JSON ↔ protobuf transcoder (L) — can run in parallel with G2

- `ProtobufTranscoder` in `VroksNet.Infrastructure.Protobuf`, behind an Application abstraction, covering decision 2.
- Descriptors are cached per specification and descriptor-set hash.
- **Done when:** the generated-code oracle passes for every fixture message, in both directions, and malformed input (truncated frames, wrong wire types, invalid UTF-8 in `string`) gives readable errors instead of exceptions.

### G4. The gRPC mock, unary (L) — after G2 and G3

- **Serving:**
  - the gRPC port with framing, compression, deadlines and trailers (decision 6);
  - `InvokeRpcMock` through Mediator;
  - request validation;
  - the call history (kind `Mock`, request line `RPC /pkg.Service/Method`, gRPC status);
  - the one-server-per-method rule.
- **Settings:** `GET /api/system/info` and `/api/system/provider` report the gRPC port and `Grpc__PublicUrl`.
- **Contract:** `docs/container-contract.md` §3 gets port `7354`; the Dockerfile gets `EXPOSE`.
  - `scripts/verify-container-contract.sh` calls the mocked Greeter with `grpcurl` from a container image, so no install is needed on the runner.
- **AppHost:** a pinned `grpc` endpoint.
- **UI:** a hint with the gRPC address and a ready `grpcurl` command, plus a "Streaming — not mocked yet" badge on streaming RPCs.
- **Done when:**
  - an integration test calls the mock with a client generated in the test project and gets the example;
  - a malformed request is answered and recorded as a violation;
  - the contract check passes.

### G5. Send through a `Grpc` connection (M) — after G3; uses G4 as its test target

- The `Grpc` connection type (moved here from G1): its `ServiceTypeTraits` entry with `OperationShape.Rpc`, `CanListen: false` and a note saying gRPC has no channel to listen on.
- `GrpcBrokerAdapter` (decision 7).
- The optional send context in `IMessageSender`, and the executor's RPC branch: gRPC status, decode, validation.
- The UI offers `Grpc` connections for RPC operations.
- **Done when:**
  - an integration test sends to VroksNet's own gRPC mock and passes;
  - a scenario whose mock answers with a mismatched message (a different spec imported under the same full method name) reports a violation;
  - E2E shows the result badge.

### G6. Server reflection (S) — after G4

- Decision 8.
- **Done when:** `grpcurl -plaintext <host>:7354 list` and `describe` work with no `.proto` on the client. The contract check covers it.

### G7. Streaming (M) — after G4/G5; not in the first release

- **Mock:**
  - server streaming sends the example N times (an option per operation, default 1);
  - client streaming reads all messages, validates each and answers once;
  - bidi answers each incoming message.
- **Send:** client and server streaming. Bidi Send stays out of scope.

### G8. Polish (S)

- `docs/guides/contract-testing/` pages for gRPC.
- The runbook.
- Configuration export: the `.proto` files go into the package's `specs` folder; the descriptor set isn't exported.

```
G0 ── G1 ── G2 ──┐
       └─ G3 ────┴─ G4 ── G5
                    ├─ G6
                    └─ G7 (optional)      G8 (any time after G4)
```

## Out of scope

- gRPC-Web and Connect protocol.
- gRPC-JSON transcoding (`google.api.http` annotations).
- TLS on the mock port.
- Error scenarios (answering with a chosen non-`OK` status). That's the same deferred feature as `X-Mock-Scenario` for HTTP (project brief, "Deliberately out of the MVP").
- Publishers and Listen for gRPC.

## Risks

- **The transcoder is the main risk.** Proto3 JSON mapping has many edge cases: well-known types, 64-bit integers, `NaN`/`Infinity`, default values. The generated-code oracle (decision 2) is the safety net. G3 is sized L for that reason.
- **h2c through proxies and Aspire.** Some proxies downgrade to HTTP/1.1, which breaks gRPC. G0 checks the container case, and the runbook documents it.
- **Contract v1 grows.** A new port and new values in the connection-type list are additive, which v1 allows. The Aspire package needs a release to expose the `grpc` endpoint.

## Decisions

Answered on 2026-10-04:

1. **Spec title:** the `package` plus the sorted service names — `shop.orders.v1 (Orders, Payments)`; the file name when there's no package (decision 4).
2. **Operation key:** `RPC /package.Service/Method` (decision 5).
3. **Streaming:** the first release is unary only. Streaming RPCs are listed with a badge and answer `UNIMPLEMENTED`; G7 comes later.
4. **Port:** `7354`, overridable with `Grpc__Port`.
5. **Multi-file specs:** in the UI from the start — G2 accepts several `.proto` files or a `.zip`, not only provisioning folders.
