# VroksNet.Domain

Project-level rules. Solution-wide rules (dependency direction, coding rules) are in `.claude/CLAUDE.md`.

- `VroksNet.Domain` — Clean Architecture Domain layer. No project references.
- **Domain** — entities, value objects, domain events, domain exceptions, enums. No framework or infrastructure references (no EF Core, no ASP.NET, no HTTP clients). This is the innermost layer and has no project dependencies of its own.
  - **Operation ↔ connection-type compatibility is enforced, not assumed.** `VroksNet.Domain.TestScenarios.OperationCompatibility.IsCompatible(operationKey, serviceType)` — reused by `CreateTestScenarioHandler`/`UpdateTestScenarioHandler` (throws `ArgumentException`, same validation-error convention as `CreateConnectionHandler`'s blank-name check — surfaces as a plain 500 today, a known pre-existing gap, not something newly introduced here) and by `MessageSender` itself at send time (returns a failed `MessageSendResult` instead of throwing — a `TestScenario`'s `Connection` can be edited independently after the scenario was saved, so the mismatch can appear later even if it didn't at creation).
