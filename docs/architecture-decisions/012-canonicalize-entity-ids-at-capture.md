# ADR 012 — External entity ids are canonicalized once, at the point of capture

**Status:** Accepted
**Date:** 2026-07-19
**GitHub issues:** #190, #207

---

## Context

This project already has a documented rule (CLAUDE.md's "GUID/enum/id comparisons are case-insensitive
by default") that every id-matching `WHERE`/`JOIN` clause must compare case-insensitively
(`UPPER(column) = UPPER(@param)`), because `.NET` serializes `Guid` lowercase by default while stored
values are consistently uppercase, and a curator-authored explicit id is under no obligation to match
that casing. That rule was found and fixed piecemeal across `status`/`entityType`/`batchId` (#154), a
conversation `{id}` route (#69), and Sources'/People's own id-first lookup (#180) before being generalised
— each time, the fix was applied at the point a query *reads* an id.

While investigating a live bug found during #190's own T2 pass (`GET /api/v1/masterdata/sources/{id}`
404ing for a Source created with a file-authored explicit id), a second, distinct failure mode was
found: **the read-side rule assumes the stored data is already canonical, but nothing enforces that on
write.** `ImportActionPlanner` threads a curator-authored explicit id (`SourceEntry.Id`,
`PersonEntry.Id`) through in-memory batch indexes (`sourceIndex`, `personIndex`) and into
`SystemImportAction.EntityId` in whatever raw casing the file used. Every downstream insert/update that
binds that id as a Dapper parameter does so with a plain `string`-typed parameter — never `Guid`-typed —
so `GuidHandler`'s uppercase normalization (`.ToString("D").ToUpperInvariant()`, applied automatically to
every `Guid`-typed parameter) never runs. The row is written to disk with whatever casing the file
happened to use.

This was masked rather than caught, because two separately-written columns that both derive from the
same uncanonicalized in-memory value (`Sources.Id` and `Quotes.SourceId`, both sourced from the same
`sourceIndex` entry) stay accidentally consistent with each other — a plain `JOIN Sources s ON s.Id =
q.SourceId` still matches, because both sides carry the same wrong casing. The inconsistency only becomes
visible when a *different* code path compares against the same column with a canonically-uppercased
value — exactly what `GuidHandler` produces for any `Guid`-typed parameter, and exactly what surfaced the
masterdata `GetById` 404.

**A read-side-only fix would have made this worse.** The first fix considered — wrapping the failing
`SELECT` in `UPPER()`, matching the existing read-side rule — was rejected after tracing the actual write
path: it would have fixed the one symptom while leaving the row non-canonical on disk, permanently
dependent on every *other* current and future query against that column remembering to do the same
wrapping. A second fix considered — uppercasing only the Source's own insert — was also rejected after
finding that `Quotes.SourceId` is written from the *same* raw value: canonicalizing one side without the
other would have broken the Quote→Source join outright, a more severe and more silent regression than the
bug being fixed.

---

## Decision

**An id originating outside this codebase's own generation (a JSON file's explicit `id` field, a URL path
segment, any other externally-supplied string) is canonicalized to this project's standard uppercase form
exactly once, at the single earliest point it is captured** — before it is used for anything: indexed
into an in-memory lookup, staged as a value another entity's write will reference, or written to any
table. Every value derived from that capture point (a same-batch FK reference, a staged
`SystemImportAction.EntityId`, an inserted primary key) inherits the canonical form for free, because
nothing downstream re-derives it from the original raw string.

This is stricter than, and does not replace, the existing read-side rule. Both are required, because they
guard against different failure modes:

- **Read-side** (`UPPER(column) = UPPER(@param)`): protects a query that compares an incoming value
  against data that is already correctly stored, when the incoming value's casing can't be controlled
  (a URL segment, a lookup keyed by an external system).
- **Write-side** (this ADR): protects the *stored data itself* from ever becoming non-canonical in the
  first place, so that every future read — whether or not its author remembered to wrap it in `UPPER()`
  — simply works.

An id that is generated internally (`EntityIdentity.StableId`'s SHA-256-derived ids, `Guid.NewGuid()`)
is already canonical by construction and needs no additional handling.

**A `Guid`-typed parameter is not sufficient proof of canonicalization on its own.** `GuidHandler`
normalizes any `Guid`-typed parameter at bind time, but an id that is threaded through the system as a
plain `string` (as `SystemImportAction.EntityId` and the `*ActionPayload` records necessarily are, since
they must also carry not-yet-existing stable ids computed before any row exists) bypasses that entirely.
The canonicalization in this ADR must happen in application code, at capture, independent of whichever
Dapper parameter type a downstream query happens to use.

### Mechanism: a single reusable helper, not a repeated `.ToUpperInvariant()` idiom

This project already has a working precedent for turning a discipline ("always do X") into something a
test suite can enforce, rather than trusting every future author to remember it: `Sql.cs`'s SQL
centralization plus `SqlQueryGuardTests`, which reflects over every query-producing method and drives it
through a full input matrix. Id canonicalization gets the same treatment, at the same layer (domain-
agnostic infrastructure, `Quotinator.Data` — matching `Optional<T>`/`FieldMergeResolver`'s own placement
reasoning), rather than being left as an idiom each Core call site is expected to repeat correctly:

- **`Quotinator.Data.Helpers.EntityIdCanonicalizer.Canonicalize(string rawId)`** — parses `rawId` as a
  `Guid` and returns `.ToString("D").ToUpperInvariant()`, the exact form `GuidHandler` produces for a
  `Guid`-typed parameter. Parsing (not a bare `.ToUpperInvariant()` on the raw string) also rejects a
  malformed id at the earliest possible point, rather than silently storing garbage. This is the single
  place this logic is allowed to exist — a capture site calls this, it does not reimplement it.
- **`EntityIdCanonicalizerTests`** (`Quotinator.Data.Tests`) — direct correctness coverage: casing
  normalization, idempotence (canonicalizing an already-canonical id is a no-op), and that a malformed
  id throws rather than silently producing a wrong value.
- **A cross-entity regression guard**, data-driven across every entity type that accepts a file-authored
  explicit id (Source, Person, StageDirection, SoundCue, Conversation — `[DynamicData]`-driven, one test
  method covering all five, matching `SqlQueryGuardTests.AssembledQueryCases`'s shape rather than five
  independently-written near-duplicate tests) — asserting the full, real invariant end to end: importing
  a lowercase-authored explicit id results in a canonically-uppercase stored row, reachable via a
  `Guid`-typed lookup, *and* still correctly joined to by whatever else references it in the same batch
  (the Quote→Source join specifically, per the incident that found this bug). A helper-level unit test
  proves the helper is correct; this guard proves the helper is actually being used everywhere it needs
  to be — the same gap `SqlQueryGuardTests` closes for `AddOpenApi`/transformer registration, not just
  the transformer class in isolation.

A stronger, purely-structural guarantee — changing `SystemImportAction.EntityId`/`*ActionPayload`'s id
fields from `string` to `Guid` throughout, so `GuidHandler` canonicalizes every binding automatically
with no possibility of a capture site forgetting to call `EntityIdCanonicalizer` — was considered and
is not ruled out, but is a substantially larger refactor across the whole staged-action pipeline. It is
noted here as a future option, not decided or required by this ADR.

---

## Reasoning

### Fixing the read side alone is treating a symptom, not the disease

CLAUDE.md's own "No exception-based migration recovery" section already establishes this project's
general preference: "fix the root cause instead of adding a check." A `UPPER()`-wrapped query is a check
— it tolerates bad data instead of preventing it. Every un-audited query against the same column remains
a landmine. Canonicalizing at capture removes the landmine entirely rather than requiring it be
rediscovered and individually defused, query by query, indefinitely.

### Two write paths deriving from one shared value must be fixed together, or not at all

The Quotes.SourceId/Sources.Id incident is the concrete proof that partial fixes in this area are not
just incomplete — they are actively dangerous. Before touching any write path that consumes an
externally-supplied id, every other write path that consumes the *same in-memory value* must be
identified and included in the same change. This is why the follow-on issue (#207)
requires the fix to be verified against the Quote→Source join specifically, not only the endpoint that
first surfaced the bug.

### Verify before implementing, every time this bug class recurs

This is the third time this specific bug class (id casing) has been found and generalised in this
project's history (#154, #69/#180, and now this ADR). Each prior instance was fixed locally, at the
query that exhibited the symptom, and each time a later instance was found in a sibling query the
original fix hadn't covered. This ADR exists specifically so the next instance is checked against a
written rule instead of being independently rediscovered and independently reasoned through again.

---

## Consequences

- The follow-on issue (#207, spawned from #190's T2 pass) must build `EntityIdCanonicalizer` and its
  guard tests as described above, then canonicalize
  `ImportActionPlanner`'s capture points for `SourceEntry.Id`/`PersonEntry.Id` through it (both the Add
  path and, separately, the correction-match path — which has its own related risk of seeding a
  same-batch index from the file's casing rather than the matched row's actual stored id) — and must
  audit every `Ensure*ExistsAsync` helper and every `Sql.*.Insert`/`Update` binding an entity id as a raw
  `string` for the same gap.
- `docs/database-conventions.md`'s new "Entity id casing" section references this ADR alongside the
  existing case-insensitive-query rule, so a future entity with a file-authored explicit id
  (`StageDirection`/`SoundCue`/`Conversation` already have one; any future entity that adds one) checks
  this ADR before assuming read-side tolerance is sufficient.
- This does not require a schema or migration change — it is an application-code discipline, enforced at
  the point external ids enter the system, not a database constraint.
