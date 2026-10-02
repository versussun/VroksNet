---
paths:
  - "src/VroksNet.Application/**"
---

# VroksNet.Application

The Clean Architecture Application layer: use cases, orchestration, DTOs, validation, and Mediator requests/handlers. It references only `VroksNet.Domain`.

## Rules

- **Depend only on Domain.** Define interfaces here (repositories, external services) for Infrastructure to implement. No concrete infrastructure types in this project.
- **Use [martinothamar/Mediator](https://github.com/martinothamar/Mediator) for all use-case dispatch**, not MediatR. It is source-generator based and uses no reflection.
- **Call `AddMediator(...)` only from inside Application**, via `AddApplication()`. Never call it from ApiService or any other project (see Gotchas).
- **Group requests + handlers by feature**, e.g. `Application/Orders/CreateOrder/`, with one request + handler pair per file group.
- **Requests and notifications are `sealed record`s** implementing `IRequest<TResponse>`, `IRequest` or `INotification`.
- **Handlers are `sealed class`es** implementing `IRequestHandler<,>` / `IRequestHandler<>` / `INotificationHandler<>`. `Handle` keeps the interface's name, with no `Async` suffix.
- **Put cross-cutting concerns (validation, logging, transactions) in `IPipelineBehavior<,>`** implementations registered via `options.PipelineBehaviors = [...]`. Don't scatter them across handlers.
- **Bump Mediator versions deliberately.** They are pinned exactly in `Directory.Packages.props` (Central Package Management rejects floating versions):

  ```xml
  <PackageVersion Include="Mediator.Abstractions" Version="3.0.2" />
  <PackageVersion Include="Mediator.SourceGenerator" Version="3.0.2" />
  ```

## Canonical shape

```csharp
// VroksNet.Application/ApplicationServiceCollectionExtensions.cs
public static IServiceCollection AddApplication(this IServiceCollection services)
{
    services.AddMediator(options =>
    {
        options.ServiceLifetime = ServiceLifetime.Scoped; // matches VroksNet.Infrastructure's scoped DbContext usage
    });
    return services;
}

// VroksNet.ApiService/Program.cs
builder.Services.AddApplication();
```

```csharp
public sealed record CreateOrder(string CustomerId, decimal Amount) : IRequest<OrderId>;

public sealed class CreateOrderHandler(IOrderRepository repository) : IRequestHandler<CreateOrder, OrderId>
{
    public async ValueTask<OrderId> Handle(CreateOrder request, CancellationToken cancellationToken)
    {
        // orchestrate domain logic, call repository
    }
}
```

## Gotchas

- **Why `AddMediator` must live in Application:** the source generator only inspects `AddMediator` calls inside its own compilation, which is the project referencing `Mediator.SourceGenerator`. A call from ApiService is invisible to it, so the generator bakes in the default Singleton lifetime, and startup then throws `Invalid configuration detected for Mediator... generated code for 'Singleton' lifetime, but got 'Scoped'`. This has happened before; don't reintroduce it. If Web ever gets its own composition root, it still has to go through `AddApplication()`.

## Feature notes

- **Test Scenarios** (`TestScenario` entity, `Application/TestScenarios/*`, `/api/test-scenarios`, the "Test Scenarios" Web page): a saved "send this message somewhere". It combines one operation (`MockEndpointId`) from one specification (`SpecificationId`), sent through one `Connection` (`ConnectionId`), with an optional `PayloadOverride`. Sending goes through `IMessageSender`/`MessageSender`, the sibling of `ConnectionTester`. It uses the same per-`ConnectionServiceType` branching but actually publishes/POSTs. See "Connection testing" in `.claude/rules/infrastructure.md`.
  - **Don't add FK constraints from `TestScenario`** to the specification, operation or connection it references. This is the same loose coupling as `CallRecord`. When a reference is deleted, the scenario fails gracefully at run time: `RunTestScenarioHandler` returns an unsuccessful result and doesn't throw. `ListTestScenariosHandler`'s `TestScenarioSummary` shows `"(deleted specification)"` / `"(deleted connection)"` placeholders instead of crashing.
  - **Every run logs a `CallRecord`** through `ICallRecordRepository`; the call history (`Application/CallRecords/*`, `/api/call-records`, the "Call History" page) reads them back. The record uses `CallDirection.OutboundHttpRequest` (an `Http`-connection run is neither `InboundHttpRequest` nor `OutboundBrokerPublish`) and sets `CallRecord.ConnectionId`, `TestScenarioId`, `StatusCode` and the contract outcome (`ContractValid`/`ValidationErrors`).
  - **`TestScenario.Kind`: `Send` or `Listen`.** `Send` is the HTTP request or broker publish; whether it's HTTP or broker follows from the connection type. `Listen` (AsyncAPI operations only) waits on the channel through `IMessageListener`, then validates the message against the payload schema (`MockEndpoint.ResponseSchema`). A new scenario's default mode comes from `TestScenarioListening.DefaultKindFor`: an AsyncAPI `send` operation defaults to Listen. Keep these rules in Domain. Web gets them through `MockEndpointDetail` (`RequiresHttpConnection`, `CanListen`, `DefaultTestScenarioKind`) and must not re-derive them from the operation key. Listen doesn't hear our own Send on the same RabbitMQ operation: Send publishes to the default exchange, which can't be bound. Validation of listen settings lives in `TestScenarioTargetResolver.ValidateListenSettings`.
  - **An `Http` run's response is validated against the spec** (contract-testing "Фаза B", see `docs/contract-testing-plan.md` 4.2). The schema is picked by the *actual* status code via `MockEndpoint.TryGetDeclaredResponse` (exact code, then `"2XX"`, then `"default"`), and an undeclared status is itself a violation. A violation fails the run: `Success`/`LastRunSuccess` mean "sent *and* matched the spec", and `RunTestScenarioResult.ContractValidation` says which part failed. It is null when nothing was checked, i.e. a broker publish, a failed send, or an operation with no stored responses (imported before Phase B, so re-import the spec).
- **Call History** (`Application/CallRecords/*`, `/api/call-records`, the "Call History" Web page): every Test Scenario run and every inbound mock call, newest first.
  - **Page with the keyset cursor, never offsets.** `ListCallRecords` returns `NextCursor`; new records arriving between requests must not shift pages.
  - **`InvokeMockEndpointHandler` logs every mock call, matched or not** (`InboundHttpRequest`, with `RequestLine`). It also decides the response body (the example, or `"{}"`), so the history holds the body that was served; for a 404 it holds the detail text, which the endpoint wraps in ProblemDetails. Don't move that choice back into the endpoint.
  - **Mock logging is best-effort.** A failed history write is logged as a warning and the mock still answers; never let it turn a matched mock call into a 500.
  - **Cap stored bodies with `CallRecordSnapshot.Truncate`** (64K chars plus a "…(truncated)" marker) wherever a `CallRecord` is written. The history list (`CallRecordSummary`) carries no bodies; `GetCallRecord` returns them for one record.
  - **Resolve display names through `ICallRecordNameResolver`** (id → name projections for just the page's ids). Don't go back to listing every specification, connection and scenario per page.
  - **`ICallRecordRepository.InsertAsync` completes only once the row is written** (`IDbWriteQueue.EnqueueAsync` awaits the job), so a record is readable as soon as the call that wrote it returns. No polling needed in tests.
- **Provider mode** (`Application/Mocking/Set*ProviderMode`, `InvokeMockEndpoint.ProviderMode`; contract-testing "Фаза D").
  - On the provider port only operations with `ServeAtRealPath` answer; `/mock` keeps answering every enabled operation.
  - The incoming body is validated against `RequestSchema` in both modes, and the outcome only goes to the history. The response never depends on it. Only JSON content types (`application/json`, `*+json`) are validated. HEAD matches the GET operation.
  - `SetServeAtRealPathAsync` returns the rows it updated. Fewer than asked (a concurrent re-import replaced the endpoints) must be a refusal (409), never a reported success.
  - Re-importing a spec must keep `ServeAtRealPath` for operations whose key is unchanged (`ImportOpenApiSpecHandler`).
- **Enabling/disabling an operation** (`Application/Mocking/SetEndpointEnabled`, `PUT /api/mock-endpoints/{id}/enabled`, the "Mock enabled" switch on the operation card).
  - Only HTTP operations can be switched (AsyncAPI ones aren't served by the mock). A disabled operation answers 404 on both surfaces; its `ServeAtRealPath` is left as it was.
  - `SetEnabledAsync` returns the rows it updated, and fewer than asked is a 409 refusal, same as provider mode.
  - Re-importing a spec keeps `IsEnabled = false` for operations whose key is unchanged (`ImportOpenApiSpecHandler`).
- **Response templating** (`IResponseTemplateEngine`, contract-testing "Фаза F", see `docs/contract-testing-plan.md` 4.6).
  - `InvokeMockEndpointHandler` renders `ExampleTemplate` with a `TemplateContext` built from the match (`OperationKeyMatcher.TryMatch` gives the path parameters) and the request (`QueryParameters`, `Headers`, `Body`), and answers with `MockEndpoint.ExampleStatusCode ?? 200`. Keep that choice in the handler; the endpoint only maps `MockInvocationResult.StatusCode`/`ResponseBody` to HTTP.
  - A status that can't carry a body (204, 304) answers with an empty `ResponseBody`, and the endpoint writes no body for it. Don't render or send `"{}"` there: Kestrel refuses to write it.
  - An unfillable placeholder never fails the call. Its warning goes to `CallRecord.Warnings` (a JSON string array, null when there are none), next to `ValidationErrors`.
