---
name: create-branch
description: Create a new git branch for VroksNet following this repo's naming convention, branched off an up-to-date default branch. Use when the user asks to start work on a feature/fix/chore, or says "create a branch", "new branch", "start a branch for X".
---

# Create branch

Create a correctly-named branch off an up-to-date default branch, and switch to it.

## Steps

1. Run `git status` and `git branch --show-current` to see the current state. If there are uncommitted changes that don't belong on the new branch, stop and ask the user how to handle them (stash, commit first, or bring them along) rather than guessing.
2. Determine the default branch (`main` if present, else `master` — check with `git branch -a` / `git remote show origin` if unclear).
3. If a remote exists, `git fetch origin` then update the default branch: `git checkout <default>` and `git pull`. If there's no remote (this repo may not have one yet), just make sure the local default branch is checked out.
4. Work out the branch type and a short description:
   - If the user gave a ticket/issue number, include it.
   - Otherwise infer type + description from what the user asked for.
   - Ask the user only if the intent is genuinely ambiguous — don't ask just to confirm an obvious choice.
5. Name the branch `<type>/<kebab-case-summary>` (add a leading ticket id if there is one, e.g. `feature/vrk-123-order-cancellation`):
   - `feature/` — new functionality
   - `fix/` — bug fix
   - `chore/` — tooling, deps, config, non-production code
   - `refactor/` — behavior-preserving code change
   - `docs/` — documentation only
   - `test/` — tests only
   - `perf/` — performance work
   - Keep the summary short (3-6 words), lowercase, hyphen-separated, no punctuation.
6. Create and switch to it: `git checkout -b <type>/<summary>`.
7. Report the branch name created. Do not push it — only push when the user asks.
