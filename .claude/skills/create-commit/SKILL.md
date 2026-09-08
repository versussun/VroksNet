---
name: create-commit
description: Stage and commit the current changes in VroksNet with a Conventional Commits message. Use when the user asks to "commit", "commit this", "create a commit", or "save these changes" as a commit.
---

# Create commit

Produce one well-formed commit for the current changes. Never amend an existing commit or use `--no-verify`/`--no-gpg-sign` unless the user explicitly asks.

## Steps

1. Run `git status` and `git diff` (and `git diff --staged` if something is already staged) to see exactly what changed. Run `git log --oneline -5` to match the repo's existing message style.
2. Decide what belongs in this commit:
   - If unstaged changes are all part of one logical change, stage them (`git add <files>` — prefer explicit paths over `git add -A`/`.` when the working tree has unrelated changes too).
   - If the changes clearly mix unrelated concerns, ask the user whether to split into multiple commits rather than bundling them.
3. Write the message in **Conventional Commits** form:
   - Subject: `type(scope): short imperative summary`, ≤72 chars, no trailing period.
     - Types: `feat`, `fix`, `chore`, `refactor`, `docs`, `test`, `perf`, `build`, `ci`.
     - Scope is optional — use the project/layer touched (e.g. `application`, `apiservice`, `mediator`) when it adds clarity.
   - Blank line, then a body explaining *why*, not a restatement of the diff — only include a body when the subject line isn't self-explanatory.
   - Footer: end with the required attribution line (see below).
4. Never invent a commit message that claims more than the diff shows (no "fixes bug" if you're not sure it fixes anything — describe what changed).
5. Commit with a heredoc so the message isn't mangled by shell quoting, e.g.:
   ```
   git commit -m "$(cat <<'EOF'
   feat(application): add CreateOrder command handler

   Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
   EOF
   )"
   ```
6. Run `git status` after committing to confirm a clean tree (or show what's left uncommitted, if intentional).
7. Do not push — only push when the user explicitly asks.
