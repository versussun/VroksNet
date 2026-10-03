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

- **Enforce operation ↔ connection-type compatibility; don't assume it.** Use `VroksNet.Domain.TestScenarios.OperationCompatibility.IsCompatible(operationKey, serviceType)`. It has two callers:
  - `CreateTestScenarioHandler`/`UpdateTestScenarioHandler` throw `ArgumentException`, the validation-error convention every create/update handler follows. The endpoints (scenarios, connections, Publishers) map it to a 400 with the reason.
  - `MessageSender` checks again at send time and returns a failed `MessageSendResult` instead of throwing. A `TestScenario`'s `Connection` can be edited after the scenario was saved, so the mismatch can appear later even if it didn't exist at creation.
- **Provider mode refuses overlapping operations.** `VroksNet.Domain.MockEndpoints.OperationOverlap.Overlaps` is the single rule: same method, same segment count, every segment pair equal or at least one a parameter. Both provider-mode handlers check it through `Application/Mocking/ProviderModeRules`, so any request on the provider port maps to exactly one operation.
