# ADR 010 — Repository is C#-only; tooling scripts follow the same rule as application code

**Status:** Accepted
**Date:** 2026-07-11
**GitHub issue:** #159

---

## Context

Quotinator is a C# / .NET project end to end — application code, tests, and the one existing utility
script (`scripts/changelog.csx`, run via `dotnet-script`). Despite that, one-off tooling tasks during
development sessions repeatedly reached for a non-C# tool instead: `python -c "..."` (not installed
on the development machine, fails outright), `perl -0pi -e "..."` for a multi-line find/replace,
`node -e "require(...)"` to pretty-print a JSON file, and `sed -i 's/.../.../g'` for a bulk rename —
each treated as an isolated slip and corrected individually, without ever asking why the same class
of mistake kept recurring in different disguises.

The recurring root cause was framing this as "which scripting language is installed on this
particular machine" — a fact about one contributor's development environment, not about the
repository itself. That framing is wrong on two counts: (1) it's session/environment-dependent
(whatever happens to be installed locally), so it gives no durable answer for a different
contributor, a CI runner, or a future session on a different machine; (2) it treats one-off tooling
as exempt from the project's own language choice, when a script that manipulates repository content
is exactly the kind of thing that should be reviewable, testable, and maintainable the same way any
other code in this repository is.

---

## Decision

**This repository is C#-only. Any script or tool whose logic is worth keeping follows the same rule
application code does — it is written in C#, and it is committed to the repository, not thrown away
as a shell one-liner.**

Concretely:

- **Reusable or non-trivial scripting logic** (data transformation, bulk find/replace across many
  files, report generation, anything with actual logic beyond a single command invocation) is written
  as a `dotnet-script` `.csx` file under `scripts/` (matching the existing `scripts/changelog.csx`
  precedent) or, if it grows beyond a single-file script, a proper C# console project under `tools/`
  (matching `tools/Quotinator.Tools.DbInspector`'s precedent — dev-only, never shipped, never
  referenced by `src/`).
- **Direct invocation of already-installed CLI tools is not "a script" and is unaffected by this
  policy.** Running `git`, `dotnet build`/`test`/`publish`, `docker build`/`run`, or `gh` via
  PowerShell or the Bash tool is normal command execution, not tooling logic — the policy governs
  what gets *written*, not which shell dispatches an existing command.
- **No Python, Perl, Node.js, or Unix text-processing one-liners (`sed`, `awk`, etc.) anywhere in
  this repository or its tooling** — not as committed scripts, not as ad hoc one-off commands during
  development, regardless of whether the tool happens to be installed on a given machine. If a task
  seems to need one, it needs a `.csx` script or a small C# tool instead.
- **PowerShell remains the primary shell** for command invocation (per the development environment),
  but PowerShell scripting itself is not a substitute for the C#-only rule above once logic beyond a
  single command is involved — a multi-line PowerShell data-transformation script has the same
  reviewability problem as a Python one and should also become a `.csx` script instead.

---

## Consequences

**A durable, environment-independent answer.** "Is Python installed on this machine" is no longer
the question — the answer is always the same regardless of who or what is working in this repo.

**Every script becomes reviewable, testable application code.** `scripts/changelog.csx` already
demonstrates the shape this takes: a `.csx` file with clear inputs/outputs, runnable via
`dotnet-script`, committed alongside the code it operates on. A future script inherits that same
standard rather than being written throwaway.

**Higher friction for genuine one-off tasks.** Writing a `.csx` file for something that would
otherwise be a five-second Python one-liner is slower in the moment. The friction is intentional —
the same reasoning ADR 006 uses for high-friction `[Parallelize]` opt-in applies here: it forces the
question "is this worth keeping as a real script?" rather than defaulting to whatever's fastest to
type. For a task that is genuinely one-shot and disposable (not worth a committed script), a direct,
literal CLI command (via `dotnet`, `git`, etc., with no embedded scripting logic) remains available —
the policy targets *scripting logic*, not command execution.

**AI assistants working in this repository must follow the same rule.** This closes the gap that
caused the four recurring incidents this ADR responds to — see `CLAUDE.md`'s Developer Context /
Commands section for the cross-reference.
