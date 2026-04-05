# Codex

## EOL Policy

Do not change line endings. This repo uses LF (`\n`) only; never introduce CRLF.
Make sure you dont use tools for which you are not 100% certain preserve the EOL of the file correctly.

## Core Principles

**Scope Principle:** Implement everything required to achieve and verify the goal - nothing more, nothing less. If you can't test it, don't write it.

**No Unrequested Work:** Don't add features, refactor surrounding code, or add "improvements" that weren't requested. Use existing patterns in the codebase, don't invent new ones.

**Read Before Writing:** Never propose changes to code you haven't read. Check how similar problems are solved elsewhere in the codebase.

## Avoid Spinning Wheels

Stop and ask when stuck rather than randomly tweaking code.

**Signs you're stuck:** Randomly tweaking code hoping it compiles/passes, fixing one thing breaks another, trying variations without understanding why.

**Default mode:** A few attempts is fine, but don't spin wheels. Stop early for non-trivial blockers.

**Autonomous mode** (user says "try your best, I'll be back" or similar):
- Try harder before stopping, but still stop if truly going in circles
- If blocked on one task, move to other independent tasks in the plan
- Maximize progress on work that doesn't depend on the blocker
- Leave clear notes about what you tried and where you're stuck

## Formatting

- ASCII7 only in documentation - no emojis, no Unicode
- No markdown tables - use plain lists
- Follow existing code style in the file you're editing

## Local Temp Folders

- Use `_tmp/` for local runtime/cache/scratchpad artifacts.
- Store local app data under `_tmp/localappdata/`.
- Keep all of the above out of commits.

## Build And Validation

- Local toolchains are not set up in this workspace.
- Do not rely on local build/test execution for final verification.
- Use GitHub Actions CI as the source of truth for build and validation status.

- For `gh` on Windows in this workspace, set local app data to a repo-local path first:
  - `New-Item -ItemType Directory -Force _tmp/localappdata | Out-Null`
  - `$env:LOCALAPPDATA = (Resolve-Path .\_tmp\localappdata).Path`

- Preferred workflow:
  - Trigger CI: `gh workflow run ci.yml -R prenaux/sourcegit --ref master`
  - Wait for completion: `gh run watch <run-id> -R prenaux/sourcegit --exit-status`
  - Read failures: `gh run view <run-id> -R prenaux/sourcegit --log-failed`
