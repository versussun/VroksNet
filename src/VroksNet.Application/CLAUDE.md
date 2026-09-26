# VroksNet.Application

Project-level rules. Solution-wide rules (dependency direction, coding rules) are in `.claude/CLAUDE.md`.

- `src/VroksNet.Application` — Clean Architecture Application layer. References `VroksNet.Domain`; carries the Mediator package references (see below).
- **Application** — use cases, orchestration logic, DTOs, validation, and the Mediator requests/handlers (see below). Depends only on Domain. Defines interfaces (repositories, external services) that Infrastructure implements — dependency inversion, no concrete infrastructure types here.

## Mediator: martinothamar/Mediator

Use [martinothamar/Mediator](https://github.com/martinothamar/Mediator) (source-generator based, no reflection) for all use-case dispatch — not MediatR.

**Packages** — already added to `VroksNet.Application` and pinned centrally in `Directory.Packages.props` (Central Package Management rejects floating versions, so bump this exact pin deliberately rather than using a wildcard):

```xml
<PackageVersion Include="Mediator.Abstractions" Version="3.0.2" />
<PackageVersion Include="Mediator.SourceGenerator" Version="3.0.2" />
```

**Registration — `AddMediator(...)` must be called from `VroksNet.Application` itself, not from `VroksNet.ApiService`.** The Mediator source generator only inspects `AddMediator` calls made within its own project's compilation (the one referencing `Mediator.SourceGenerator`, i.e. Application) — a call from a downstream project like ApiService is invisible to it, so the generator bakes in the default (Singleton) lifetime regardless of what's requested there, and the app throws at startup (`Invalid configuration detected for Mediator... generated code for 'Singleton' lifetime, but got 'Scoped'`) the moment the two disagree. This bit us once already — don't reintroduce it.

The fix already in place: Application exposes its own DI extension, and `Program.cs` just calls that:

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
```
```csharp
// VroksNet.ApiService/Program.cs
builder.Services.AddApplication();
```

If `VroksNet.Web` ever gets its own composition root, it still can't call `AddMediator` directly for the same reason — go through `AddApplication()`.

**Requests and handlers** live in Application, grouped by feature (e.g. `Application/Orders/CreateOrder/`), one request + handler pair per file-group:

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

- Requests/notifications are `sealed record`s implementing `IRequest<TResponse>`, `IRequest`, or `INotification`.
- Handlers are `sealed class`es implementing `IRequestHandler<,>` / `IRequestHandler<>` / `INotificationHandler<>`.
- Cross-cutting concerns (validation, logging, transactions) go in `IPipelineBehavior<,>` implementations registered via `options.PipelineBehaviors = [...]`, not scattered across handlers.
- Presentation calls only `IMediator.Send(...)` / `IMediator.Publish(...)`; it never calls a handler directly.

## Feature notes

- **Test Scenarios** (`TestScenario` domain entity, `Application/TestScenarios/*`, `/api/test-scenarios`, the "Test Scenarios" Web page): a saved "send this message somewhere" — one operation (`MockEndpointId`) from one specification (`SpecificationId`), sent through one `Connection` (`ConnectionId`), with an optional `PayloadOverride`. Reuses `ConnectionTester`'s sibling, `IMessageSender`/`MessageSender` (same per-`ConnectionServiceType` branching, same direct RabbitMQ.Client/NATS.Client.Core usage — see the "Connection testing" bullet in `src/VroksNet.Infrastructure/CLAUDE.md` — but actually publishing/POSTing instead of just pinging).
  - **No FK constraints from `TestScenario` to the specification/operation/connection it references** — same loose-coupling as `CallRecord` already had. Deleting any of those leaves a scenario that fails gracefully at run time (`RunTestScenarioHandler` returns an unsuccessful result, doesn't throw) rather than cascading; `ListTestScenariosHandler`'s denormalized `TestScenarioSummary` shows `"(deleted specification)"`/`"(deleted connection)"` placeholders for a dangling reference instead of crashing.
  - **Every run logs a `CallRecord`** (`ICallRecordRepository`, write-only for now — nothing reads these back yet, per docs/project-brief.md Phase 04 "история и логи") with the new `CallDirection.OutboundHttpRequest` value (added alongside the pre-existing `InboundHttpRequest`/`OutboundBrokerPublish` — an `Http`-connection `TestScenario` run is neither of those) and the new `CallRecord.ConnectionId` column.
