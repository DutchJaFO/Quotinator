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

**Never assert a total count of notifications.** The same failure mode, and it has already produced
two wrong expectations. How many notifications exist depends on which producers exist and what the
bundled changelog flags for the running version, both of which move every milestone. Assert instead
that the notification a **known cause** produces is present — a successful import, a failed import, an
upgrade with notification-flagged highlights, a schema-version overshoot. The subject is the *cause*,
not the notification row as an object. Where a scenario genuinely is about counting, count occurrences
of that specific notification, never the total.

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

## Test outcomes feed the Knowledgebase

A test creates a specific circumstance on purpose and shows what it produces. That is exactly what a
Knowledgebase entry needs — with the cause established by construction rather than inferred from a log
line, and the remedy already exercised rather than reasoned about.

The `Observed effect` field is what captures it. #333 sweeps these documents alongside its sweep of
the application's own messages, and writes entries from what they record. No entry is written here.

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

Being moved — sections 8–11 and 19–27 of the previous suite are not here yet.

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

---

## Document template

Every test document follows this shape.

```markdown
# <What this verifies>

**Smoke:** yes | no
**Traces to:** #NNN[, #NNN]

## Preconditions

The exact state the setup must reach before any assertion below means anything, and how that state is
confirmed — not inferred from the recipe having been followed.

## Determinism

Every variable the outcome depends on, and how each is pinned.

## Steps

The commands, in order.

## Expected output

What each command must produce for a pass.

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
