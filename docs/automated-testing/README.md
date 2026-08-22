# Automated Testing — the T2 suite

These are the project's automated integration tests: everything that needs a real container, a real
SQLite file, a real volume, or the real bundled dataset to mean anything. Unit tests cover what can be
proven in-process; a test earns a place here by exercising something they cannot reach.

Run against a locally built Docker image. See [`docs/release-verification.md`](../release-verification.md)
for the tier definitions and [`CLAUDE.md`](../../CLAUDE.md)'s Pre-Push Checklist for when a pass is
required.

---

## When to run what

Three scopes, and they are not interchangeable.

| Scope | What runs |
|---|---|
| **End of an issue (T2)** | The designated smoke set below, plus whatever tests are relevant to that issue. Not everything |
| **End of a milestone** | Every test here. No exceptions |
| **Release** | Every test here — a release follows a milestone close |

An issue's "relevant tests" are the ones covering what it touched. Deciding that is the issue's own
job, recorded in its plan doc; the smoke set is the floor underneath it, never a substitute for it.

---

## The designated smoke set

Nine tests. The question this set answers is *does this container fundamentally work* — a test is in
it because its failure would invalidate most other results, not because it is important in its own
right.

| Test | Category |
|---|---|
| Baseline — health/version/random/search | `api-surface/` |
| Pagination contract | `api-surface/` |
| Import and staged-action review workflow | `import-and-staged-actions/` |
| Fresh seed produces zero pending actions | `import-and-staged-actions/` |
| Per-file, per-entity-type import/seed report | `import-and-staged-actions/` |
| Reset is a full wipe with no reseed | `database-lifecycle/` |
| Startup wait page during database initialisation | `startup-and-degradation/` |
| Startup notification system | `notifications-and-changelog/` |
| Changelog is served from its own on-disk database | `notifications-and-changelog/` |

Every test document states its own membership in its `Smoke` field. This table is the index of that
field, not a second source of truth — if the two disagree, the document is right and this table needs
fixing.

---

## Rules every test here follows

**A test must reproduce its condition reliably. An intermittent result is not a result.** If a test
passes or fails depending on something it does not pin, it is not testing what it claims to. This is
what the `Determinism` field exists for: name every variable the outcome depends on, and pin it.

Two measured cases are why this is a rule rather than advice. #326 found that WAL sidecar state — not
a pending migration — decides whether a read-only mount degrades, so a test that does not pin how the
seeding container was stopped passes or fails by luck. The source-download path has produced both
outright failures and ~300 ms successes minutes apart. A condition that cannot be pinned is reported
as such, not left in the suite as a coin flip.

**Never assert a specific migration number or schema version.** Not `Data v2 → v11`, not "migration 8
does X". Migration counts change whenever any milestone adds one, and they are consolidated before a
release, so a hardcoded number goes stale on its own and gets "fixed" by editing the number rather
than by anyone checking what actually happened. Assert the *behaviour*: that a `migration applied:`
line appears, that no SQLite error accompanies it, that the resulting state is healthy, that content
is present and correct.

**We only observe facts. We never claim to know how many.** A test cannot legitimately assert that an
import produces 799 quotes or 461 sources — that is a prediction about content, and content changes
when a bundled file is updated, a converter changes, or a user import lands. Asserting it means the
test fails for a correct reason, gets "fixed" by editing the digit, and absorbs a real regression the
next time one occurs.

What can be asserted are the facts the operation itself establishes:

- **Zero import failures.** This is the invariant, and it is the same rule as zero errors and zero
  warnings at compile time. Observably: the call succeeded, nothing threw, and the report shows zero in
  the buckets that mean something needs attention — `Pending`, `Blocked`, `Stale`.
- **Non-zero where content must exist.** An import into an empty database must produce at least one
  quote. That is a fact about the operation, not a prediction about the dataset.
- **Relationships that hold whatever the data is.** One seed batch per bundled file; the manifest linked
  to every batch it drove; a count unchanged across a restart; a read reporting the same number the
  write reported. These stay true when the dataset changes, because they are derived in the same run.

**Stable resources are the exception, not the default.** A fixture owned by a test — created from
scratch, or captured from a real database at the moment a bug is discovered — exists only where a
specific feature or issue **cannot be tested any other way**. Do not build one because a count looks
fragile: the bundled sources are what actually ships, and a test against them is testing the real
thing.

Two situations meet that bar. A feature whose condition cannot be reached through the application at
all — the state has to be constructed. And a test whose precondition comes from a path that can itself
break, where a fixture is what keeps the test runnable while that path is being fixed (see *Depending
on content is not the same as depending on another test* above).

**Never assert a total count of notifications.** The same failure mode, and it has already produced
two wrong expectations. How many notifications exist depends on which producers exist and what the
bundled changelog flags for the running version, both of which move every milestone. Assert instead
that the notification a **known cause** produces is present — a successful import, a failed import, an
upgrade with notification-flagged highlights, a schema-version overshoot. The subject is the *cause*,
not the notification row as an object. Where a scenario genuinely is about counting, count occurrences
of that specific notification, never the total.

**A test must not depend on which other tests ran, or in what order.** The only order that matters is
the sequence of steps *inside* a test. Anything a test needs, it establishes itself — the rows it pages
through, the batch it applies, the state it corrupts. A test that inherits its preconditions from a
predecessor reports a false result the moment that predecessor is skipped, reordered, fails, or is
deleted, and it cannot be run alone to investigate anything.

This applies to what a test *leaves behind* as much as what it needs. Registered overrides, staged
batches and applied migrations are state the next test did not ask for. Clean up, or scope the test to
its own container and volume.

**Prose like "run the import tests first" or "the tests that follow" is the symptom, not the fix.**
Turning such a sentence into a link makes the dependency official rather than removing it — the fix is
always to give the test its own setup.

### Depending on content is not the same as depending on another test

Some tests need a database with content before they mean anything — the pagination contract needs rows
to page through, the masterdata reads need records to read. That is a legitimate precondition, and it
is not what the rule above forbids. What it forbids is *how* the precondition gets satisfied: by
whatever an earlier test happened to leave behind.

A precondition has exactly two honest resolutions, and a document states which one it uses:

1. **Guarantee it.** The test establishes the content itself — its own import, its own fixture, its own
   container. It then runs anywhere, in any order, including alone.
2. **Accept that it can be blocked.** If the content comes from the application's own import path, then
   a broken import blocks this test too, and it cannot run until that is fixed. Say so, and name what
   would block it, so a skipped run reads as a known consequence rather than an unexplained gap.

**Option 2 is why a prepared resource can be worth building.** A test whose precondition depends on a
feature that is currently broken is a test you lose exactly when you most want to run it — the
bundled-import path failing takes the endpoint tests down with it, even though the endpoints may be
perfectly fine. A fixture that does not go through that path keeps them runnable. That is a specific
feature this cannot otherwise be tested around, which is the bar the exception rule below sets.

**Prefer verification that needs no live environment.** A live test costs a container start, a human
to read the output, and a judgement call about whether the output was right. A test that runs in
`dotnet test` costs none of those and runs on every build, so anything provable in-process should be
proven there — leaving the live tier to cover only what genuinely cannot be reached without a real
container, volume or network.

This is not theoretical. Three checks have already made that move, and each got stronger for it:

- A `curl | grep` of the published OpenAPI spec became `OpenApiSpecEndpointTests`, which fetches
  `/openapi/v1.json` through the full pipeline and asserts the type via `JsonDocument`. The original
  command was wrong on its first outing — it assumed single-line JSON and matched nothing.
- A sixteen-minute wait proving the changelog database survived became
  `ChangelogDatabaseWiringTests`, asserting the DI registration is not an in-memory connection string.
  Instant, and it cannot pass for the wrong reason.
- #326's degraded-startup contract became `StartupResilienceTests`, which reaches the same states via
  `WebApplicationFactory` using deterministic sabotage.

**When a live test is written, ask what part of it needs to be live.** Often the answer is a smaller
part than the whole document, and the rest belongs in a unit or integration test that runs
continuously rather than once per release.

**Cover both the happy flow and the unhappy flow.** A test that only proves the good path proves half
a feature. What an operation does when its input is wrong, its precondition is absent, or its
dependency is unavailable is behaviour the application ships, and it is where the defects that reach
users actually live — a bare `400` where a `422` with a reason belongs, a write that silently matches
nothing, a page that 500s instead of degrading.

**A failure must be reproducible, or its consequences cannot be recorded.** This is why the unhappy
flow needs the same `Preconditions` and `Determinism` discipline as the happy one, and often more: an
error state reached by luck tells you nothing about what the application does in it. Pin how the
failure is provoked, then write down what it actually produces.

**The minimum expected consequence of any failure is a stable application.** Whatever else a wrong
input or a broken dependency causes, the process stays up, the surfaces stay reachable, and the
operator is told something they can act on. A test's unhappy flow states that outcome explicitly rather
than stopping at the error code — see the never-crash rule below, which is the same requirement stated
from the other side.

**The application must never crash.** The worst acceptable outcome of any startup problem is a
degraded UX plus an OpenAPI surface that still allows recovery. A test that provokes a startup problem
is therefore testing a *feature* — the degradation path — not merely reproducing a historical
incident. An unhandled exception, a container that exits, or a page returning 500 is a failure of that
feature regardless of what caused it.

**Wait for a condition, never for a duration.** A fixed `sleep` encodes a guess about how long a
machine takes, and it is wrong in both directions: too short and the test fails on a slower machine
for a reason that has nothing to do with what it verifies; too long and every run pays for the worst
case. Poll for the state the test actually depends on.

Two canonical waits, because tests do not all wait for the same thing:

```bash
# Wait until the app is healthy — the normal case.
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
```

```bash
# Wait until the app is listening, whatever it answers — for degraded scenarios, where
# /health returning 503 IS the expected outcome and the first form would loop forever.
until curl -s -o /dev/null http://localhost:8080/api/v1/health; do sleep 1; done
```

```bash
# Wait on the log — for a container with no published port, where neither HTTP form is available.
until docker logs <name> 2>&1 | grep -q "Quotinator ready"; do sleep 1; done
```

A wait that is genuinely for elapsed time — a TTL expiring, a refresh interval passing, confirming a
container *stayed* dead rather than became ready — is not a readiness wait and keeps its `sleep`. It
must say so in `Determinism`, naming what the duration is measuring. An unexplained `sleep` is a
guess, and the rule above applies to it.

**Refer to a test by what it verifies, never by its number.** Numbers are an index within a category
and shift when one is inserted. Cross-references between tests are links, not prose pointing at a
number.

**This list only grows.** When a pass surfaces a new bug or edge case, add its verification here in
the same commit that fixes it.

---

## Environment profiles

**A test declares the environment it needs and establishes it. It never inherits one.**

This section exists because of a measured failure. The single file this folder replaces opened with a
*Baseline* section that started the container, published the port, set the admin key and let the
first boot seed — once, for every section after it. Read top to bottom that worked. Split into one
document per test it did not: 21 of 43 documents were left driving `localhost:8080` with nothing to
answer them, most sending an admin key nothing set.

A named profile a test *invokes* is not the same thing as a predecessor a test *follows*. It is the
first of the two honest resolutions above — guarantee the precondition — written once instead of
forty-three times.

### The three profiles

| Profile | What it establishes |
|---|---|
| **Fresh** | New volume, first boot, bundled seed, nothing else |
| **Constrained** | Fresh, then one deliberate defect — read-only root, dropped table, no writable path |
| **Upgraded** | A prior image ran against this volume first, then the current build |

**Content is a separate axis and stays the test's own responsibility.** A test needing a populated
database is Fresh plus an import step it already owns — not a fourth profile. Shared *setup* is what
these profiles provide; shared *content* is what the independence rule forbids, and the distinction is
the whole reason this is safe.

**Constrained is a layer, not a third base.** It is always applied *on top of* a base — usually Fresh,
sometimes Upgraded, when the defect is only reachable in a database that an older build wrote. A
document whose environment is both writes both, base first:

```markdown
**Environment:** Upgraded + Constrained
```

#### Fresh

```bash
docker rm -f qt-env 2>/dev/null; docker volume rm qt-env 2>/dev/null
MSYS_NO_PATHCONV=1 docker run -d --name qt-env -p 8080:8080 -v qt-env:/data \
  -e Quotinator__DataDir=/data \
  -e Quotinator__AdminApiKey=<your admin key> \
  -e Quotinator__AutoPurgeBundledImportActions=false \
  quotinator:<base tag>
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
```

**Every profile pins `Quotinator__AutoPurgeBundledImportActions` explicitly, and Fresh pins it
`false`.** Left unset, the bundled batches' `Import_Action` rows are purged straight after a
successful seed — so a test concluding anything from an empty action list cannot tell its own result
apart from the purge. Three documents currently rest on exactly that. Retaining the rows makes "none
are pending" an observation instead of an artefact.

**`--name` is mandatory**, on this and on every `docker run` in the suite. Without it, every later
`docker cp` and `docker logs` is written against a `<container>` placeholder no reader can resolve.

**Never `docker run` in the foreground ahead of later steps.** `docker run --rm` without `-d` holds the
terminal, and every command after it in the block is unreachable — the previous suite's own baseline
had this defect, which is part of why nothing after it could run.

#### Constrained

Fresh, then exactly one deliberate defect, named in `Determinism`. Two kinds, and they cost different
amounts:

- **A flag** — `--read-only`, an unwritable mount, a removed env var. Free and exactly reproducible;
  nothing to snapshot.
- **A state** — a dropped table, a corrupted file, a rolled-back version counter. Worth capturing as a
  database backup, because reconstructing it by hand is where irreproducibility creeps in.

#### Upgraded

Run the prior image against a fresh volume, let it finish its own first boot, stop it, then run the
current build against the same volume. Which prior image depends on what the test is about: the
milestone base image for "does this milestone's schema change upgrade cleanly", a published tag for
"does the upgrade our users will actually perform work".

The prior boot must be waited on by **the state the test is about**, never by a duration. A fixed wait
here has already let a defect through: a 45-second check read zero notifications and looked like proof
none were written, when seeding simply had not finished.

### Snapshot and restore

A group of tests sharing a profile should not pay for a rebuild and a reseed each. Capture the
environment once, restore it between tests.

**Image.** Tag the milestone's base image and save it once:

```bash
docker tag quotinator:local quotinator:m<N>-base
docker save -o .claude/temp/test-environments/quotinator-m<N>-base.tar quotinator:m<N>-base
```

Tests run against the pinned tag. A test that must rebuild — proving a bundled file ships inside the
image, for instance — builds its own throwaway tag and never overwrites the base. This is not
hypothetical tidiness: one document edits a bundled rule file, rebuilds `quotinator:local`, and never
rebuilds after reverting, leaving three sibling tests running a mutated image.

**Database.** Captured from a **stopped** container, with the `-wal` and `-shm` sidecars, or via
SQLite's own `VACUUM INTO`. A copy taken from a running container can be torn mid-write, and a torn
copy fails as "no rows" — indistinguishable from the assertion it was meant to check.

**Restore is unconditional between tests**, never "if the test dirtied something". The moment it is
conditional, inherited state is back with a better name.

**Ordering within a group is allowed. Ordering across groups is not**, and every group must be able to
start cold. Otherwise "run the Fresh group" quietly becomes the new Baseline section.

Backups live in `.claude/temp/test-environments/` — already gitignored, and deleted once the milestone
is published. Anything longer than a couple of commands becomes a `scripts/testing/` entry per
[ADR 010](../architecture-decisions/010-repository-is-csharp-only.md) rather than growing inside a
document.

### The milestone base image is also the migration fixture

Capture it at milestone start, and the upgrade tests stop depending on a published tag existing: the
base image *is* the version this milestone upgrades from, by definition. That also removes the staleness
— a hardcoded `ghcr.io/dutchjafo/quotinator:1.8.3` only ever tests one upgrade, and stops being the
interesting one at the next release.

**State the condition when the snapshot is taken.** It is the users' upgrade path only if the milestone
branched from the released tag. That is normally true here, and it is what makes the base image
legitimate rather than merely convenient.

### Not adopted: reset-and-reseed as a cheaper restore

`POST /api/v1/admin/database/reset` followed by `POST /api/v1/admin/database/reseed` would avoid a
container restart entirely. It is not used yet, for a reason worth recording rather than rediscovering:
since #156 Reset is a full wipe that deliberately does **not** reseed, and whether the pair reproduces
a fresh first boot is unverified. Quote content should reproduce from deterministic ids, but audit
rows, notification rows, the two schema-version counters and `Import_Action` rows plausibly do not.
Prove that equivalence before the suite rests on it.

---

## Test outcomes feed the Knowledgebase

A test creates a specific circumstance on purpose and shows what it produces. That is exactly what a
Knowledgebase entry needs — with the cause established by construction rather than inferred from a log
line, and the remedy already exercised rather than reasoned about.

The `Observed effect` field is what captures it. #333 sweeps these documents alongside its sweep of
the application's own messages, and writes entries from what they record. No entry is written here.

**The unhappy flow is the richer source.** An entry answers *what happened, does it stop the app
working, and what do I do about it* — which is a description of a failure, not of a success. A test
that provokes a failure deliberately and records what an operator would see has already done the work
an entry needs; a happy-path-only test has nothing to contribute.

---

## Where things live

Test documents sit in a category subfolder, numbered per category with a stable slug —
`api-surface/02-pagination-contract.md`. Numbering restarts in each folder, so a new test appends to
its own category and disturbs nothing else.

Fixture files, seed data, and expected-output samples a test needs go in a subfolder beside its
document. Executable scripts go to `scripts/testing/`, per
[ADR 010](../architecture-decisions/010-repository-is-csharp-only.md) — never inline in the document,
never beside it.

---

## The tests

### `api-surface/`

| # | Test | Smoke |
|---|---|---|
| 01 | [Baseline — health, version, random and search](api-surface/01-baseline.md) | yes |
| 02 | [The pagination contract holds live on every paginated endpoint](api-surface/02-pagination-contract.md) | yes |
| 03 | [The Unicode-aware search flag reaches the running app](api-surface/03-unicode-aware-search-toggle.md) | no |
| 04 | [Endpoint names and summaries follow the standard](api-surface/04-endpoint-naming-and-operation-ids.md) | no |

### `identity-and-casing/`

| # | Test | Smoke |
|---|---|---|
| 01 | [A file-authored explicit id is canonicalized at capture](identity-and-casing/01-canonicalize-explicit-ids-at-capture.md) | no |
| 02 | [A quote resolves by id in either casing](identity-and-casing/02-quote-id-case-insensitive-lookup.md) | no |
| 03 | [A conversation line in the wrong casing does not violate the foreign key](identity-and-casing/03-conversation-line-quote-id-fk-safety.md) | no |
| 04 | [String-typed id fields render canonically over HTTP](identity-and-casing/04-read-time-presentation-of-string-typed-ids.md) | no |
| 05 | [Generic-repository endpoints return correct data and lowercase ids](identity-and-casing/05-generic-repository-select-list-wrapping.md) | no |

### `database-lifecycle/`

| # | Test | Smoke |
|---|---|---|
| 01 | [Captured source files reconstruct byte-for-byte](database-lifecycle/01-file-resource-capture-and-reconstruction.md) | no |
| 02 | [Audit export, date-range discovery, and conflict-data auto-purge](database-lifecycle/02-audit-export-and-conflict-data-purge.md) | no |
| 03 | [Reset wipes the entire database and does not reseed](database-lifecycle/03-reset-is-a-full-wipe.md) | yes |

### `startup-and-degradation/`

| # | Test | Smoke |
|---|---|---|
| 01 | [Seeding backup, degraded startup, and Reset recovery](startup-and-degradation/01-seeding-backup-degraded-startup-and-reset-recovery.md) | no |
| 02 | [Startup backs up only when there is real work](startup-and-degradation/02-startup-backup-gating-and-storage-budget.md) | no |
| 03 | [Kestrel serves a wait page during initialisation](startup-and-degradation/03-startup-wait-page.md) | yes |
| 04 | [Migration replay under restricted write](startup-and-degradation/04-migration-replay-under-restricted-write.md) | no |
| 05 | [Degraded pages survive a migration failure](startup-and-degradation/05-degraded-pages-survive-a-migration-failure.md) — **cannot pass; #327 is rewriting it** | no |

### `notifications-and-changelog/`

| # | Test | Smoke |
|---|---|---|
| 01 | [Notifications list, dismiss, render, and drive their action](notifications-and-changelog/01-notification-system.md) | yes |
| 02 | [Notification metadata, provenance, and the released-database migration path](notifications-and-changelog/02-notification-metadata-and-provenance.md) | no |
| 03 | [Upgrading from an intermediate schema version](notifications-and-changelog/03-upgrade-from-an-intermediate-schema-version.md) | no |
| 04 | [Upgrading enriches the legacy notification rather than duplicating it](notifications-and-changelog/04-upgrade-does-not-duplicate-the-legacy-notification.md) | no |
| 05 | [The legacy notification gains provenance](notifications-and-changelog/05-legacy-notification-provenance.md) | no |
| 06 | [A what's-new row predating release state is backfilled](notifications-and-changelog/06-whats-new-row-predating-release-state.md) | no |
| 07 | [The changelog is served from its own on-disk database](notifications-and-changelog/07-changelog-served-from-its-own-database.md) | yes |

### `import-and-staged-actions/`

| # | Test | Smoke |
|---|---|---|
| 01 | [The staged review → decide → apply workflow](import-and-staged-actions/01-staged-action-review-workflow.md) | yes |
| 02 | [A batch applied through the staged flow can be reversed](import-and-staged-actions/02-two-phase-decide-apply-reversal.md) | no |
| 03 | [`POST /import?batchId=` applies an already-staged batch](import-and-staged-actions/03-batch-id-mode-alias.md) | no |
| 04 | [Discarding a staged batch applies nothing](import-and-staged-actions/04-discard.md) | no |
| 05 | [Reversing an applied batch, and re-import resurrection](import-and-staged-actions/05-reverse-and-resurrection.md) | no |
| 06 | [A bodyless import request is rejected with an actionable message](import-and-staged-actions/06-bodyless-request-validation.md) | no |
| 07 | [StageDirection and SoundCue Modify, and Complete blocking](import-and-staged-actions/07-stagedirection-soundcue-modify.md) | no |
| 08 | [Person Modify, Complete blocking, and mixed-case id reversal](import-and-staged-actions/08-person-modify-and-lowercase-id-reversal.md) | no |
| 09 | [Character↔Source links stay per-Source](import-and-staged-actions/09-character-source-many-to-many-identity.md) | no |
| 10 | [A Source discovered from a quote carries that quote's date](import-and-staged-actions/10-source-date-from-resolving-quote.md) | no |
| 11 | [A missing `batchId` is rejected, and the log reports the real status](import-and-staged-actions/11-batch-id-validation-and-request-log-status.md) | no |
| 12 | [Character Modify, explicit ids on Add, case-insensitive Source matching](import-and-staged-actions/12-character-modify-and-explicit-id-on-add.md) | no |
| 13 | [Bulk-deciding a batch via file export and re-import](import-and-staged-actions/13-bulk-decide-via-file-export-import.md) | no |
| 14 | [A fresh seed resolves every bundled file with nothing left pending](import-and-staged-actions/14-fresh-seed-produces-zero-pending-actions.md) | yes |
| 15 | [The rule lookup reads the file's live content](import-and-staged-actions/15-rule-file-live-read-proof.md) | no |
| 16 | [A stale conflict rule stages Stale, not Decided](import-and-staged-actions/16-conflict-rule-staleness.md) | no |
| 17 | [An alias is stale only on a genuine rename](import-and-staged-actions/17-source-alias-staleness.md) | no |
| 18 | [Rule-file override endpoints and alias candidates](import-and-staged-actions/18-rule-file-override-endpoints.md) | no |
| 19 | [Every seed and import surface reports per-file counts](import-and-staged-actions/19-per-file-import-report.md) | yes |

---

## Where the previous suite's sections went

Until #339 this suite was one file, `docs/smoke-tests.md`, whose tests were numbered `1`–`44`. Plan
docs, issue comments and code comments written before the split refer to those numbers, and this table
is what resolves them. It is a permanent key, not a migration aid — those references are historical
records and are not going to stop existing.

| Old | Now |
|---|---|
| 1 | [api-surface/01-baseline.md](api-surface/01-baseline.md) |
| 2 | [import-and-staged-actions/01-staged-action-review-workflow.md](import-and-staged-actions/01-staged-action-review-workflow.md) |
| 3 | [import-and-staged-actions/02-two-phase-decide-apply-reversal.md](import-and-staged-actions/02-two-phase-decide-apply-reversal.md) |
| 4 | [import-and-staged-actions/03-batch-id-mode-alias.md](import-and-staged-actions/03-batch-id-mode-alias.md) |
| 5 | [import-and-staged-actions/04-discard.md](import-and-staged-actions/04-discard.md) |
| 6 | [import-and-staged-actions/05-reverse-and-resurrection.md](import-and-staged-actions/05-reverse-and-resurrection.md) |
| 7 | [import-and-staged-actions/06-bodyless-request-validation.md](import-and-staged-actions/06-bodyless-request-validation.md) |
| 8 | [import-and-staged-actions/07-stagedirection-soundcue-modify.md](import-and-staged-actions/07-stagedirection-soundcue-modify.md) |
| 9 | [import-and-staged-actions/08-person-modify-and-lowercase-id-reversal.md](import-and-staged-actions/08-person-modify-and-lowercase-id-reversal.md) |
| 10 | [import-and-staged-actions/09-character-source-many-to-many-identity.md](import-and-staged-actions/09-character-source-many-to-many-identity.md) |
| 11 | [import-and-staged-actions/10-source-date-from-resolving-quote.md](import-and-staged-actions/10-source-date-from-resolving-quote.md) |
| 12 | [identity-and-casing/01-canonicalize-explicit-ids-at-capture.md](identity-and-casing/01-canonicalize-explicit-ids-at-capture.md) |
| 13 | [api-surface/02-pagination-contract.md](api-surface/02-pagination-contract.md) |
| 14 | [identity-and-casing/02-quote-id-case-insensitive-lookup.md](identity-and-casing/02-quote-id-case-insensitive-lookup.md) |
| 15 | [identity-and-casing/03-conversation-line-quote-id-fk-safety.md](identity-and-casing/03-conversation-line-quote-id-fk-safety.md) |
| 16 | Folded into [identity-and-casing/02](identity-and-casing/02-quote-id-case-insensitive-lookup.md) — it was an explanatory note, not a test |
| 17 | [identity-and-casing/04-read-time-presentation-of-string-typed-ids.md](identity-and-casing/04-read-time-presentation-of-string-typed-ids.md) |
| 18 | [identity-and-casing/05-generic-repository-select-list-wrapping.md](identity-and-casing/05-generic-repository-select-list-wrapping.md) |
| 19 | [import-and-staged-actions/11-batch-id-validation-and-request-log-status.md](import-and-staged-actions/11-batch-id-validation-and-request-log-status.md) |
| 20 | [import-and-staged-actions/12-character-modify-and-explicit-id-on-add.md](import-and-staged-actions/12-character-modify-and-explicit-id-on-add.md) |
| 21 | [import-and-staged-actions/13-bulk-decide-via-file-export-import.md](import-and-staged-actions/13-bulk-decide-via-file-export-import.md) |
| 22 | [import-and-staged-actions/14-fresh-seed-produces-zero-pending-actions.md](import-and-staged-actions/14-fresh-seed-produces-zero-pending-actions.md) |
| 23 | [import-and-staged-actions/15-rule-file-live-read-proof.md](import-and-staged-actions/15-rule-file-live-read-proof.md) |
| 24 | [import-and-staged-actions/16-conflict-rule-staleness.md](import-and-staged-actions/16-conflict-rule-staleness.md) |
| 25 | [import-and-staged-actions/17-source-alias-staleness.md](import-and-staged-actions/17-source-alias-staleness.md) |
| 26 | [import-and-staged-actions/18-rule-file-override-endpoints.md](import-and-staged-actions/18-rule-file-override-endpoints.md) |
| 27 | [import-and-staged-actions/19-per-file-import-report.md](import-and-staged-actions/19-per-file-import-report.md) |
| 28 | [api-surface/03-unicode-aware-search-toggle.md](api-surface/03-unicode-aware-search-toggle.md) |
| 29 | [startup-and-degradation/01-seeding-backup-degraded-startup-and-reset-recovery.md](startup-and-degradation/01-seeding-backup-degraded-startup-and-reset-recovery.md) |
| 30 | [database-lifecycle/01-file-resource-capture-and-reconstruction.md](database-lifecycle/01-file-resource-capture-and-reconstruction.md) |
| 31 | [database-lifecycle/02-audit-export-and-conflict-data-purge.md](database-lifecycle/02-audit-export-and-conflict-data-purge.md) |
| 32 | [database-lifecycle/03-reset-is-a-full-wipe.md](database-lifecycle/03-reset-is-a-full-wipe.md) |
| 33 | [notifications-and-changelog/01-notification-system.md](notifications-and-changelog/01-notification-system.md) |
| 34 | [api-surface/04-endpoint-naming-and-operation-ids.md](api-surface/04-endpoint-naming-and-operation-ids.md) |
| 35 | [startup-and-degradation/02-startup-backup-gating-and-storage-budget.md](startup-and-degradation/02-startup-backup-gating-and-storage-budget.md) |
| 36 | [startup-and-degradation/03-startup-wait-page.md](startup-and-degradation/03-startup-wait-page.md) |
| 37 | [startup-and-degradation/04-migration-replay-under-restricted-write.md](startup-and-degradation/04-migration-replay-under-restricted-write.md) |
| 38 | [startup-and-degradation/05-degraded-pages-survive-a-migration-failure.md](startup-and-degradation/05-degraded-pages-survive-a-migration-failure.md) |
| 39 | [notifications-and-changelog/02-notification-metadata-and-provenance.md](notifications-and-changelog/02-notification-metadata-and-provenance.md) |
| 40 | [notifications-and-changelog/03-upgrade-from-an-intermediate-schema-version.md](notifications-and-changelog/03-upgrade-from-an-intermediate-schema-version.md) |
| 41 | [notifications-and-changelog/04-upgrade-does-not-duplicate-the-legacy-notification.md](notifications-and-changelog/04-upgrade-does-not-duplicate-the-legacy-notification.md) |
| 42 | [notifications-and-changelog/05-legacy-notification-provenance.md](notifications-and-changelog/05-legacy-notification-provenance.md) |
| 43 | [notifications-and-changelog/06-whats-new-row-predating-release-state.md](notifications-and-changelog/06-whats-new-row-predating-release-state.md) |
| 44 | [notifications-and-changelog/07-changelog-served-from-its-own-database.md](notifications-and-changelog/07-changelog-served-from-its-own-database.md) |

**This table does not license numbering a test again.** Refer to a test by what it verifies; the
numbers here exist only to resolve references written before the split.

---

## Document template

Every test document follows this shape.

```markdown
# <What this verifies>

**Smoke:** yes | no
**Environment:** Fresh | Constrained | Upgraded
**Traces to:** #NNN[, #NNN]

## Preconditions

The exact state the setup must reach before any assertion below means anything, and how that state is
confirmed — not inferred from the recipe having been followed.

The named profile covers the environment. This field states only what is true *beyond* it — the
content this test imports for itself, the defect it introduces, the prior version it upgrades from.
A document that needs nothing beyond its profile says so.

## Determinism

Every variable the outcome depends on, and how each is pinned.

## Steps

The commands, in order — the happy flow and the unhappy flow, not one of them.

## Expected output

What each command must produce for a pass, including what the unhappy flow produces and how the
application behaves while producing it.

## Observed effect

What this circumstance actually produces: the log lines, health response, UI state and messages an
operator would see. Distinct from Expected output, which states only what must be true for a pass.

## Cleanup

How to return the machine to a clean state.
```

**`Preconditions` and `Determinism` are the two fields that exist because of a specific failure.** Two
sections of the file this folder replaces carried identical setup and asserted opposite outcomes — one
that the app stayed healthy, one that it degraded — and neither confirmed it had reached its own
premise. The one asserting degradation could never have passed. It went unnoticed because each section
described its setup in prose, in a single file nobody read end to end.

A test whose `Preconditions` cannot be confirmed, or whose `Determinism` cannot be pinned, is a finding
about the test. Report it rather than writing the document anyway.
