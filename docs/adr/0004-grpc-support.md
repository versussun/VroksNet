# ADR 0004 — gRPC support

**Status:** Proposed (2026-10-04)
**Date:** 2026-10-04
**Related:** `docs/grpc-support-plan.md` (steps G0–G8 that carry this out), ADR 0003 (adapters and `ServiceTypeTraits`), `docs/contract-testing-plan.md` (the contract-testing types), `docs/container-contract.md` §3 (ports)

## Context

VroksNet mocks and contract-tests REST (OpenAPI) and brokers (AsyncAPI). Services also talk over gRPC, described by `.proto` files. Supporting them touches every layer:

- **Specs:** `ISpecificationParser` reads YAML/JSON documents only. A `.proto` file is a different language, often split over several files with `import`s.
- **Messages:** everything downstream of import works on JSON — examples (`SchemaExampleGenerator`), templating (`ResponseTemplateEngine`), validation (`ISchemaValidator`) and the call history. gRPC messages are protobuf binary.
- **Operations:** `OperationCompatibility` tells HTTP from AsyncAPI keys by a space. A gRPC full method name (`/pkg.Service/Method`) has none, so it would be taken for AsyncAPI.
- **Serving:** the mock and the provider port speak HTTP/1.1. gRPC needs HTTP/2, length-prefixed frames and a status in trailers.

The plan's open questions were answered on 2026-10-04: the title is the package plus the sorted service names; operation keys are `RPC /pkg.Service/Method`; the first release is unary only; the gRPC port is `7354`; several `.proto` files or a `.zip` can be uploaded in the UI from the import step.

A spike (step G0, 2026-10-04) checked the riskiest assumptions. Its findings are recorded under each decision.

## Decision

### Parse `.proto` in managed code with `protobuf-net.Reflection`

- `protobuf-net.Reflection` 3.4.30 turns `.proto` text into a `FileDescriptorSet`. No `protoc` binary in the image.
- **Spike findings:**
  - The well-known types (`google/protobuf/timestamp.proto`, `wrappers.proto`, `empty.proto`, …) resolve without the user supplying them: the library embeds them.
  - Errors carry the file, line and column, e.g. `broken.proto(4,13): type not found: 'strin'` and `broken.proto(3,8): unable to find: 'missing/thing.proto'`.
  - Imports resolve from memory through the library's `IFileSystem`, provided `AddImportPath("")` is called. So one implementation serves both the provisioning folder and a multi-file or `.zip` upload.
  - `protobuf-net.Reflection` declares its own `Google.Protobuf.Reflection.FileDescriptorSet`, which clashes with Google.Protobuf's. The reference uses an `extern alias`, and only the parser sees it.
- **Storage:** the parsed set, serialized as protobuf bytes, goes into one column on `ApiSpecification`. Everything after import loads it into Google.Protobuf's runtime descriptors (`FileDescriptor.BuildFromByteStrings`). That call wants dependencies before the files that import them, so the set is stored in dependency order.
- **Which files become specs:** each file that declares at least one `service`. Files without services are only imports.
- **Refused at import:** proto2 groups and extensions, with a readable error.

### Our own JSON ↔ protobuf transcoder

- **Spike finding:** Google.Protobuf 3.36.2 has no dynamic message type (no `DynamicMessage` or similar), so it can't read or write a message without generated code. protobuf-net is code-first.
- So VroksNet gets a descriptor-driven transcoder in Infrastructure, behind an Application abstraction. It reads and writes the wire format through `CodedInputStream`/`CodedOutputStream` and uses the canonical proto3 JSON mapping.
- The unit test project generates C# from its fixtures with `Grpc.Tools`, and the transcoder's bytes and JSON must equal the generated messages' `ToByteArray()` and `JsonFormatter` output.

### Messages enter the existing pipeline as JSON Schema

- At import each request and response message becomes a self-contained JSON Schema that follows the proto3 JSON mapping, stored in `MockEndpoint.RequestSchema`/`ResponseSchema`.
- The example generator, the templating engine, the validator and the call history then work unchanged.
- **Validation:** the bytes must decode as the declared message (a failure is a contract violation), then the decoded JSON is checked against the schema.

### An `Rpc` operation shape and a `Grpc` connection type

- Keys are `RPC /pkg.Service/Method`. `OperationCompatibility` checks the `RPC ` prefix before the space rule.
- `ServiceTypeTraits` replaces `bool IsHttp` with an operation shape: `Http`, `Message` or `Rpc`. `GET /api/system/connection-types` keeps `isHttp` for contract v1 and adds the shape.
- `Grpc` joins the types: Send only, with a note that gRPC has no channel to listen on.
- **Spike finding:** a raw unary call through `Grpc.Net.Client` 2.84.0 works with a `Method<byte[], byte[]>` and pass-through marshallers, over h2c and with deadlines. No generated client is needed.
  - The connection check calls `grpc.health.v1.Health/Check`. A server without the health service answers `UNIMPLEMENTED`, which still proves it's reachable.
  - `IMessageSender` gains an optional per-operation context carrying the descriptors. Only the gRPC adapter reads it.

### The mock answers on its own h2c port

- **Port:** a Kestrel endpoint on `Grpc__Port` (default `7354`) with `Protocols = Http2`. It's plaintext HTTP/2 by prior knowledge (h2c); TLS is left to a proxy.
- **Spike findings:**
  - An HTTP/2-only endpoint in the `aspnet:10.0` image, running as user `1654`, served `grpcurl` from another container on the same Docker network.
  - Hand-written framing works: read the 5-byte prefix and the message, write the response frame, then send the `grpc-status` trailer. An unknown method gets a trailers-only `UNIMPLEMENTED` response (status in the headers, no body), which both `grpcurl` and `Grpc.Net.Client` read correctly.
- **Mock calls are raw request handling**, mapped in ApiService and dispatched through Mediator (`InvokeRpcMock`). `Grpc.AspNetCore` services aren't used for them, because those need generated service classes.
- **Every RPC answers at its real path**, since gRPC clients can't add a base path. At most one enabled operation may serve each full method name; enabling a second is refused, following `OperationOverlap`.

### Server reflection on the mock port, from the stored descriptors

- **Spike findings:**
  - `grpcurl list` and `describe` worked against descriptors built at runtime from `.proto` text, served through `Grpc.AspNetCore.Server` 2.84.0. `grpc.reflection.v1` and `v1alpha` are both in the `Grpc.Reflection` package.
  - `AddGrpc()` registers a fallback that answers every unknown service with "Service is unimplemented", before our raw handler sees the call. `GrpcServiceOptions.IgnoreUnknownServices = true` turns the fallback off.
- The package's `ReflectionServiceImpl` takes a fixed list of descriptors in its constructor, but specs change while VroksNet runs. So VroksNet implements the reflection services itself on the package's generated `ServerReflectionBase` classes (v1 and v1alpha), reading the currently enabled specs on each request.
- `Grpc.AspNetCore.Server` is used only to host the reflection services. Mock calls stay raw.

### Out of scope

- Streaming in the first release. Streaming RPCs are imported and listed, and answer `UNIMPLEMENTED` until plan step G7.
- gRPC-Web and the Connect protocol.
- gRPC-JSON transcoding (`google.api.http`).
- TLS on the gRPC port.
- Answering with a chosen error status.

## Consequences

- **New dependencies,** pinned in `Directory.Packages.props`: `protobuf-net.Reflection` 3.4.30, `Google.Protobuf` 3.36.2, `Grpc.Net.Client` 2.84.0 and `Grpc.AspNetCore.Server`/`Grpc.Reflection` 2.84.0. `Grpc.Tools` is used in the unit test project only.
- **The transcoder is the largest and riskiest piece** (plan step G3). The generated-code oracle is what makes it safe to change.
- **Contract v1 grows** by a port (`7354`, Aspire endpoint `grpc`), a connection type and a field in the connection-type list. All of these are additions, which v1 allows. The Aspire package needs a release to expose the endpoint.
- **h2c through proxies:** a proxy that downgrades to HTTP/1.1 breaks gRPC. The runbook says so.
- **Re-import by title:** the title includes the service names, so adding or removing a service imports a new specification instead of replacing the old one. The import result points this out when a spec with the same package exists.
