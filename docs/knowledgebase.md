# Knowledgebase

The Knowledgebase is Quotinator's user-facing answer store: what a message means, whether it costs the
operator anything, what to do about it, and how to accomplish a task. It holds three kinds of entry —
**diagnostics** (a condition the software reports), **questions** (a direct answer), and **guides** (a
procedure) — under one lookup mechanism.

This document defines the **diagnostic code protocol**: how codes are formed, when one is allocated,
and what an entry must carry. It does not define the storage format or the in-app surface — those are
each their own issue.

**Why it exists.** Degraded and read-only operation legitimately produce log output that looks alarming
and is not. `CLAUDE.md`'s triage rule tells a *developer* to establish impact before chasing a warning;
the Knowledgebase is the same answer written down for an *operator*, who otherwise has a message and
nowhere to look it up.

---

## Code format

Two kinds of code, one scheme.

| Kind | Form | Unique? | Example |
|---|---|---|---|
| **Entry code** | `QTN-<AREA>-<NNN>` | Unique and permanent — never reused, never renumbered | `QTN-DB-014` |
| **Status code** | `QTN-<STATUS>` | Reusable — many conditions carry the same one | `QTN-INV` |

- **`QTN`** — fixed product prefix. Makes a code unmistakable and greppable in a Home Assistant
  supervisor log, where several add-ons' output interleaves.
- **`<AREA>`** — one value from the closed list below. Deliberately coarser than the
  `[Subsystem - Phase]` table in [`logging.md`](logging.md), which has 30+ entries and grows with every
  new endpoint: an area must never need renaming because code moved.
- **`<NNN>`** — zero-padded sequential integer within the area, allocated once. **Carries no meaning**,
  which is what makes it impossible for it to become wrong.

### Areas

| Area | Covers |
|---|---|
| `APP` | Process lifecycle, startup, operating mode (degraded, read-only, ready) |
| `DB` | Schema, migration, backup, database availability |
| `CFG` | Configuration and environment — data directory, environment variables, options |
| `IMP` | Import, seeding, source refresh |
| `NET` | Outbound networking — downloads, connectivity |
| `SEC` | TLS, DataProtection keys, rate limiting, security advisories |
| `API` | Conditions surfaced to an API caller |
| `UI` | Blazor rendering, circuit, ingress |

Adding an area is an amendment to this document, not something an entry does on its own.

### Severity is deliberately not encoded

The same condition can be a warning in one context and entirely expected in another — an unwritable
data directory is a fault when it is unintended and correct when read-only mode was asked for. A code
that encoded severity would have to change when the context did, which is exactly what a stable
identifier must not do. Severity belongs to the log line and to the entry's own triage answer, never to
the identifier.

---

## Allocation is decided by triage

Not every message gets a code. The decision runs through `CLAUDE.md`'s triage question — *does it
prevent the application or the API from functioning?* — and the answer is recorded on the entry:

| Triage answer | What it gets |
|---|---|
| **Prevents the app or API functioning** | An entry code. The operator needs the remedy. |
| **Does not prevent it** | An entry code *if* an operator is likely to see it and worry. The entry's job is to say "this costs you nothing" — for expected noise in a degraded mode, that *is* the answer they need. |
| **Impact unknown** | An entry code, a status code, **and** a GitHub issue. Unknown impact is an unanswered question, not "harmless by default"; this is the case status codes exist for. |

Routine Debug output — request logs, asset logs — never gets a code. **A code is a promise that looking
it up produces an answer**, so allocating one for a line nobody would ever look up devalues every other
code.

---

## Status codes

A small set of reusable words, not numbers, because their value is that a beta tester understands them
without a lookup.

| Status code | Means |
|---|---|
| `QTN-INV` | Aware, and actively investigating |
| `QTN-KNOWN` | Aware and tracked, not currently being worked |
| `QTN-EXPECTED` | Normal in this operating mode — not a fault |

**This set is expected to grow.** Adding one is an amendment to this table, and needs only that it names
a state genuinely distinct from those already here — not a shade of one of them. Words, never numbers:
a numbered status would need the same lookup the code exists to avoid.

### Status lives on the entry, not in the binary

An entry's status changes; a status code compiled into a log message does not. Ship `QTN-INV` in a beta,
fix the cause, and the binary still claims an investigation is under way.

So the authoritative status is the **entry's**, which is mutable and is what an operator looks up. A
status code appears in a log line only where immediate recognition is the point — a beta release, where
a tester seeing `QTN-INV` should not have to look anything up to know the report is already in.

Because entries carry their GitHub issue, that is enforceable rather than a matter of remembering:

> A guard test asserts that no entry marked investigating references a **closed** GitHub issue, and that
> no source-embedded status code refers to an entry whose issue is closed.

Same idiom as `TranslationCompletenessTests` and the schema-drift guards — the scheme maintains itself
instead of relying on someone stripping a marker before release.

---

## Typed, never stringly-typed

Every fixed value in this protocol is an **enum or a constant**, so using the wrong one is a compile
error rather than a code that silently matches no entry:

| Value | Type | Why |
|---|---|---|
| Area | `enum` | The area list is closed. A typo'd `"DBB"` must not compile. |
| Status | `enum` | Same, and the set grows by amendment — an enum makes every consumer of a new member visible immediately. |
| Entry code | `const string`, one per condition | Referenced by the log line, health response, notification body and degraded UI alike. A literal typed twice is a literal that drifts. |

Enums live in their own `Enums/` folder per project, per
[ADR 016](architecture-decisions/016-class-naming-suffixes-and-enum-placement.md).

**Status is persisted, so [ADR 008](architecture-decisions/008-enum-backed-columns-require-check-constraints.md)
applies in full** — the column carries a `CHECK (… IN (…))` enumerating the enum's members, the
baseline SQL matches, and the schema-drift CHECK-constraint test is updated. Adding a status member is
therefore a migration, which is a feature rather than friction: it is what stops a member existing in
C# and being rejected by the database at runtime.

Rendering is a separate concern from the type. An enum member renders to its wire form
(`Investigating` → `QTN-INV`) through one mapping, not by each call site formatting it — see
`logging.md` for how a code reaches a log line.

---

## What an entry carries

| Field | Notes |
|---|---|
| Entry code | `QTN-<AREA>-<NNN>`, or absent for a question/guide that describes no specific condition |
| Kind | Diagnostic, question, or guide |
| Symptom | What the operator actually sees — verbatim log text or UI message, where there is one |
| **Does it prevent the app or API from functioning** | Answered explicitly. The point of the entry, not a nicety |
| Cause | |
| Remedy | |
| Status | From the status-code table, when one applies |
| GitHub issue | When one exists — see the workflow rule below |
| Affected versions | When the entry is version-specific |
| CVE | When the entry concerns a security advisory — see below |
| Retired | Marks an entry as no longer applicable. **Entries are retired, never deleted**, so a code can never be reused |

---

## Security advisories

A CVE that actually affects Quotinator may warrant a Knowledgebase entry, so an operator can find out
whether it affects *them* and what to do about it.

**The Knowledgebase does not replace the existing CVE records.** `docs/security/README.md` and the
per-project `src/[project]/CVE/CVE-YYYY-NNNNN.md` files (see [`workflow/cve.md`](workflow/cve.md)) stay
the technical record. A Knowledgebase entry is the user-facing counterpart — *does this affect me, and
what do I do* — and links to that record rather than restating it. Two documents saying the same thing
in different words is exactly the drift this project avoids elsewhere.

Such an entry gets an ordinary `QTN-SEC-<NNN>` code so the lookup mechanism stays uniform, and carries
the real CVE identifier in its own field.

---

## Relationship to the `[Subsystem - Phase]` prefix

They answer different questions for different readers, and neither replaces the other:

| | Answers | For |
|---|---|---|
| `[Subsystem - Phase]` | Which part of the system emitted this | A developer grepping a log |
| `QTN-<AREA>-<NNN>` | What this means and what to do about it | An operator looking it up |

A coded log line carries both — see [`logging.md`](logging.md)'s "Knowledgebase codes in a log line".

This is **not** a reintroduction of numeric `EventId`s, which `logging.md` rules out: an `EventId` is
log-navigation metadata on a `[LoggerMessage]` attribute, whereas a Knowledgebase code is user-facing
text that appears in the message itself and in every other surface the same condition reaches — the
health response, a notification body, the degraded UI. That the same code appears in all of them is
what makes it a lookup rather than prose-matching, and what lets it survive translation and rewording.
