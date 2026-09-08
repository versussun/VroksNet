# VroksNet — Project Rules

.NET 10 / .NET Aspire solution. These rules govern how code is structured and written in this repo. Follow them by default; call out explicitly when a change would violate one.

## Solution layout

- `src/VroksNet.AppHost` — Aspire orchestration only (no business logic).
- `src/VroksNet.ServiceDefaults` — shared cross-cutting Aspire wiring (telemetry, health checks, resilience).
- `src/VroksNet.Domain` — Clean Architecture Domain layer. No project references.
- `src/VroksNet.Application` — Clean Architecture Application layer. References `VroksNet.Domain`; carries the Mediator package references (see below).
- `src/VroksNet.Infrastructure` — Clean Architecture Infrastructure layer. References `VroksNet.Application`.
- `src/VroksNet.ApiService` — HTTP API host (Presentation layer / composition root). References `VroksNet.Application` and `VroksNet.Infrastructure`, registers the mediator in `Program.cs`.
- `src/VroksNet.Web` — Blazor/web frontend (Presentation layer).
- `tests/VroksNet.Tests` — test project.

`VroksNet.Web` is Presentation too: if/when it needs to call use cases directly (rather than only through `VroksNet.ApiService`'s HTTP API), give it the same `Application`/`Infrastructure` references and composition-root treatment as `VroksNet.ApiService` — never let it reach into Infrastructure types directly from component code.

## Architecture: Clean Architecture

Dependencies point inward only. Outer layers depend on inner layers; inner layers know nothing about outer ones.

```
Domain  ←  Application  ←  Infrastructure
                ↑
          Presentation (ApiService / Web)
```

- **Domain** — entities, value objects, domain events, domain exceptions, enums. No framework or infrastructure references (no EF Core, no ASP.NET, no HTTP clients). This is the innermost layer and has no project dependencies of its own.
- **Application** — use cases, orchestration logic, DTOs, validation, and the Mediator requests/handlers (see below). Depends only on Domain. Defines interfaces (repositories, external services) that Infrastructure implements — dependency inversion, no concrete infrastructure types here.
- **Infrastructure** — implementations of Application's interfaces: persistence (EF Core, repositories), external service clients, file system, email, etc. Depends on Application (and transitively Domain).
- **Presentation** (`VroksNet.ApiService`, `VroksNet.Web`) — thin. Controllers/endpoints/components map HTTP or UI concerns to Mediator requests and back to responses. No business logic here; delegate to Application via the mediator. Depends on Application and Infrastructure only to wire up DI at startup (composition root) — request-handling code should only ever reach into Application.
- Never reference outward: Domain must not reference Application/Infrastructure/Presentation; Application must not reference Infrastructure or Presentation.

## Mediator: martinothamar/Mediator

Use [martinothamar/Mediator](https://github.com/martinothamar/Mediator) (source-generator based, no reflection) for all use-case dispatch — not MediatR.

**Packages** — already added to `VroksNet.Application` and pinned centrally in `Directory.Packages.props` (Central Package Management rejects floating versions, so bump this exact pin deliberately rather than using a wildcard):

```xml
<PackageVersion Include="Mediator.Abstractions" Version="3.0.2" />
<PackageVersion Include="Mediator.SourceGenerator" Version="3.0.2" />
```

**Registration** — already wired up in `Program.cs` of `VroksNet.ApiService` (the composition root):

```csharp
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped; // default is Singleton; use Scoped if handlers touch scoped services (e.g. DbContext)
});
```

If `VroksNet.Web` ever gets its own composition root, register the mediator there the same way.

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

## .NET coding rules

- **Seal by default.** Every class is `sealed` unless it is explicitly designed as a base class for inheritance (i.e. it already has, or is deliberately meant to have, derived classes). If you're not designing for extension, seal it — this includes handlers, services, and DTOs.
- **Nullable reference types** enabled everywhere (already set per-project) — don't suppress warnings with `!` unless truly unavoidable; add a comment when you do.
- **File-scoped namespaces** (`namespace Foo.Bar;`) in all new files.
- **Prefer records** for immutable data (DTOs, Mediator requests/notifications, value objects). Prefer primary constructors for simple dependency injection into classes.
- **`async`/`await` all the way** — no `.Result`/`.Wait()`/`.GetAwaiter().GetResult()` on async code; suffix async methods with `Async` outside of Mediator handlers (`Handle` keeps the interface's name).
- **One public type per file**, file name matches the type name.
- Favor expression-bodied members for trivial one-liners; keep everything else in a normal method body for readability.
- No business logic in Presentation projects — it belongs in Application (orchestration) or Domain (rules/invariants).
