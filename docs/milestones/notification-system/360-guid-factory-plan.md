# #360 — Migration-generated identifiers are not valid UUIDs; route all id creation through one factory

**Status:** Planning
**GitHub issue:** #360
**Tiers required:** T1, T2
**Depends on:** none

> **Next action: refine this plan into a verification checklist, then write the red tests.** The
> requirements are settled in the issue; what is not yet decided is where the SQLite function is
> registered so it is available to every connection *before* migrations run, which the Steps cannot be
> written around until it is answered.

---

## Background

A migration cannot call `Guid.NewGuid()` — its text is handed straight to SQLite — so every migration
that invents a primary key hand-writes the same construction:

```sql
lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-' ||
lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(6)))
```

That is 16 random bytes sliced 4-2-2-2-6 and hyphenated: a GUID's shape with none of its structure. A
v4 UUID carries `4` in the version nibble and one of `8`/`9`/`a`/`b` in the variant nibble; both are
random here. Measured against SQLite, the version nibble came back as `f`, `0`, `1`, `2` and `4` across
six consecutive values.

Nothing fails today, because `Guid.Parse` ignores both fields. The cost is that the project has three
id-generation strategies where its documentation implies two, and `schemas/source-flat.schema.json`
tells a reader every `id` is a UUID v4.

The expression is in five files and reached the fifth by being copied, which is the actual defect: no
mechanism stops a sixth.

## Verified against the code before planning

- **The generated value is not a v4 UUID** — measured, not inferred, by evaluating the expression
  against a scratch SQLite database (see the issue for the six sampled values).
- **`SqliteConnection.CreateFunction` is already used here.** #222 registered `UNICODE_CONTAINS` behind
  the `Quotinator:UnicodeAwareSearch` flag, so a project-supplied SQL function is an established
  mechanism rather than a new one. That registration is the precedent to follow for placement.
- **`testing-policy.md` already requires a hex letter in any casing fixture**, prefers one that
  *starts* with a letter, and records #213 as the live case where an all-digit literal silently proved
  nothing. Requirement 4 moves that from a rule each test must remember into something the factory
  guarantees.
- **The five sites are** `QuotinatorMigrations`, `AuditMigrations`, `ImportConflictMigrations`,
  `NotificationLegacyMetadataMigrations` and `NotificationTranslationMigrations`.

## Governing standards

| Standard | Bearing on this issue | Where |
|---|---|---|
| [ADR 012](../../architecture-decisions/012-canonicalize-entity-ids-at-capture.md) | Every id is canonical lowercase; the factory and the SQL function both emit that form | Requirement 3 |
| ADR 015's frozen-migration rule | The five existing sites are in applied migrations, which is why they are exempted rather than rewritten | Requirement 6 |
| CLAUDE.md — DI policy | The factory is injected, never `new`ed at a call site | Requirement 1 |
| CLAUDE.md — string centralisation | The function's SQL name is a named constant, not a literal repeated per migration | Requirement 5 |
| `docs/testing-policy.md` — casing fixtures | The factory's case-testing identifier satisfies the hex-letter rule by construction | Requirement 4 |
| A new ADR | The resulting rule constrains every future migration | Requirement 8 |

## Open decision — for the developer, not to be assumed

**Where the SQLite function is registered.** It must be present on every connection *before* the first
migration executes, which places it earlier than `UNICODE_CONTAINS`'s own registration point. Whether
that belongs in the connection factory, in `DapperConfiguration`, or in `DatabaseInitializer`'s own
setup path decides what the Steps look like, so the Steps are not written until it is settled.

## The red-first sequence this plan is held to

**Every step writes its tests, runs them, and records them red before any of that step's code exists.**
Not a restatement of `process.md` for its own sake — it is here because #319 finished with three tests
that could not honestly tick its first Definition-of-done box, and that box has to be ticked before an
issue closes. A plan that lets implementation land first produces an issue that permanently reads as
unfinished, and the ordering cannot be recreated afterwards: `testing-policy.md` is explicit that a
mutation check validates a test's sensitivity and is *not* a substitute for the red-before-fix run.

Two consequences for the Steps below, once written:

- **Each step's `**Status:**` line does not move to ✅ until its tests were observed red first.** A step
  whose first test run was green is recorded as such immediately, not discovered at closing time.
- **A test named in the issue's `Expected tests` table is written under that exact name**, or the
  deviation is agreed before the step is closed. #319 reached its verification pass with two named tests
  that did not exist — one a naming difference, one a genuine untested requirement.

## Steps

Not yet written — see the open decision above. This plan is refined into numbered steps and a
verification checklist before any code, per `process.md`.

## Verification

**Not yet written — this is the first implementation step.** Recorded as an explicit gap rather than an
empty table, so the doc's own state says what the next action is. The issue's `Expected tests` table is
the starting point; each row becomes a checklist entry naming its exact test class and method.
