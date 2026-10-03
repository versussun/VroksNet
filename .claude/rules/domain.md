---
paths:
  - "src/VroksNet.Domain/**"
---

# VroksNet.Domain

The Clean Architecture Domain layer and the innermost layer. **It has no project references.**

## Rules

- **Contents:** entities, value objects, domain events, domain exceptions, enums.
- **No framework or infrastructure references:** no EF Core, no ASP.NET, no HTTP clients.
- **Put invariants and business rules here.** Application only orchestrates them.

## Feature notes

- **What a connection type is and can do lives in `VroksNet.Domain.Connections.ServiceTypeTraits`** (ADR 0003): HTTP or broker, whether it can Listen and why not, display name, value label and hint, and the AsyncAPI server protocols it speaks (`TypesFor(protocols)` → which types fit a spec; HTTP speaks none, since an AsyncAPI operation can't go through it). Rules read the traits; **never compare `ConnectionServiceType` values** to decide behaviour. Broker-specific settings are `BrokerOptions` (an immutable name → value object on `TestScenario`/`Publisher`); Domain never interprets them — the adapter of the connection's type declares and reads them. A new type needs an entry in `ServiceTypeTraits.All` and a broker adapter (`.claude/rules/infrastructure.md`); `ServiceTypeTraitsTests` and `BrokerAdapterRegistryTests` keep them consistent. `GET /api/system/connection-types` serves them to the UI.
- **Enforce operation ↔ connection-type compatibility; don't assume it.** Use `VroksNet.Domain.TestScenarios.OperationCompatibility.IsCompatible(operationKey, serviceType)`. It's checked twice:
  - `CreateTestScenarioHandler`/`UpdateTestScenarioHandler` throw `ArgumentException`, the validation-error convention every create/update handler follows. The endpoints (scenarios, connections, Publishers) map it to a 400 with the reason.
  - Each broker adapter checks the operation's shape again at send time (`IsHttpOperation`/`ChannelAddressOf`) and returns a failed `MessageSendResult` instead of throwing. A `TestScenario`'s `Connection` can be edited after the scenario was saved, so the mismatch can appear later even if it didn't exist at creation.
- **Provider mode refuses overlapping operations.** `VroksNet.Domain.MockEndpoints.OperationOverlap.Overlaps` is the single rule: same method, same segment count, every segment pair equal or at least one a parameter. Both provider-mode handlers check it through `Application/Mocking/ProviderModeRules`, so any request on the provider port maps to exactly one operation.
