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
  - `CreateTestScenarioHandler`/`UpdateTestScenarioHandler` throw `ArgumentException`. This follows the same validation-error convention as `CreateConnectionHandler`'s blank-name check. It surfaces as a plain 500 today, a known pre-existing gap.
  - `MessageSender` checks again at send time and returns a failed `MessageSendResult` instead of throwing. A `TestScenario`'s `Connection` can be edited after the scenario was saved, so the mismatch can appear later even if it didn't exist at creation.
