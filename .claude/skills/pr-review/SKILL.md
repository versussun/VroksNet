---
name: pr-review
description: Review a VroksNet pull request or the current diff against this repo's own rules (Clean Architecture layering, Mediator conventions, sealed-class rule) in addition to general code quality. Use when the user asks to "review this PR", "review the diff", "check this branch before merging".
argument-hint: [PR number | branch | (nothing = current diff)]
---

# PR review

Review $ARGUMENTS (a PR number, a branch, or — if nothing is given — the current working diff against the default branch) for correctness and quality, using both general judgment and this repo's own rules in [.claude/CLAUDE.md](../../CLAUDE.md).

## Steps

1. Run the built-in review first: invoke the `code-review` skill on the same target (PR number / branch / current diff) to get general correctness and simplification/efficiency findings. Reuse its output rather than re-deriving it.
2. Then check the diff specifically against this repo's rules — things a generic reviewer wouldn't know to check:
   - **Dependency direction (Clean Architecture)**: does any changed file in `Domain` reference `Application`/`Infrastructure`/Presentation? Does `Application` reference `Infrastructure` or a Presentation project (`VroksNet.ApiService`, `VroksNet.Web`) directly, instead of depending on an interface it defines? Flag any inward layer taking a dependency on an outer one.
   - **Business logic placement**: is there orchestration or domain logic living in `VroksNet.ApiService`/`VroksNet.Web` (Presentation) instead of being delegated to a Mediator request handled in Application?
   - **Mediator usage**: are new use cases expressed as `sealed record ... : IRequest<T>`/`IRequest`/`INotification` with a matching `sealed class ... : IRequestHandler<...>`/`INotificationHandler<...>`? Is cross-cutting logic (validation, logging, transactions) duplicated across handlers instead of factored into an `IPipelineBehavior<,>`? Does anything call a handler directly instead of going through `IMediator`? Is MediatR used anywhere instead of martinothamar/Mediator?
   - **Sealed-by-default**: is every new/changed class `sealed` unless it is genuinely designed as a base class (has or is meant to have derived types)?
   - **Other repo conventions**: file-scoped namespaces, nullable enabled with no unexplained `!` suppressions, records for DTOs/messages, no blocking on async (`.Result`/`.Wait()`/`GetAwaiter().GetResult()`), one public type per file.
3. Combine both passes into one findings list, most severe first. For each finding give the file/line, what's wrong, and why it matters (concrete failure scenario or the specific rule from CLAUDE.md it breaks) — same bar as the underlying code-review skill: no restating the diff, no nitpicks without a reason.
4. If nothing from step 2 applies (e.g. the diff touches no layered code), say so explicitly rather than omitting the section — it confirms the check ran.
