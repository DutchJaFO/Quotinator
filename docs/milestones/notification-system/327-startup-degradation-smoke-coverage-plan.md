# #327 — Smoke tests: prove startup problems degrade rather than crash

**Status:** Planning
**GitHub issue:** #327
**Tiers required:** T1, T2
**Depends on:** #326, #339

---

## Description

The application must never crash; the worst acceptable outcome of a startup problem is a degraded UX
plus an OpenAPI surface that still allows recovery. That is a feature, and the environmental tier does
not verify it.

What exists instead is a section reproducing one historical incident (#293), which forces its failure
with `--read-only` — a technique #294 subsequently made survivable. The two sections now share a setup
and assert opposite outcomes, so #293's own guard ("health must be 503, confirming the test actually
reached the failure state") can never hold.

This issue replaces it with a family of startup problems, each provoking a different failure at a
different stage, authored as documents in #339's `startup-and-degradation/` category.

**Two premises changed after this issue was filed**, both from #326 (2026-08-21): a pending migration
is not the deciding variable — WAL sidecar state is — and the contract is now verified in-process by
`StartupResilienceTests` for two sabotage techniques. What remains unverified is what no unit test can
emulate: a real container, a real volume, a real read-only mount.

---

## Steps

### 1. Confirm the three scenarios reach their own failure states

**Status:** ⬜ Not started — **do this before writing any document**

The defect this issue fixes is a test that asserted an outcome it could never reach. Do not repeat it:
establish each scenario's failure state by measurement first, then write the document around what was
observed.

- **Unwritable data directory:** mount the volume `:ro`, not `--read-only` on the root filesystem, and
  pin the WAL sidecar state by pinning how the seeding container is stopped. #326 measured that with
  `-wal`/`-shm` absent SQLite cannot open the database at all, and with them present the same database
  on the same mount reads fine.
- **Corrupt or truncated database file.**
- **Schema version ahead of the application.**

### 2. Establish the overshoot scenario's real contract

**Status:** ⬜ Not started

A database whose recorded version is ahead of the build is **not** a degradation case and must not
assert the degraded contract. `DatabaseInitializer` detects it deliberately and continues; `Program.cs`
gates the notification on `dbHealth.IsHealthy && SchemaVersionOvershootDetected`. Per #289 this is only
reachable after a migration squash, where the schema is complete and only the counter is stale.

Its contract is: process alive, `/health` **healthy**, overshoot notification present. Asserting 503
here would either fail or get "fixed" by changing correct behaviour.

No existing test puts a database into an overshoot state, so this is new coverage regardless.

### 3. Write the degradation contract into each degrading scenario

**Status:** ⬜ Not started

Process stays alive; `/health` reports unhealthy rather than being unreachable; the Blazor pages render
degraded UI rather than 500; the OpenAPI surface stays reachable.

### 4. State the recovery route, and whether it can actually succeed

**Status:** ⬜ Not started

An unwritable data directory cannot be repaired by a Reset — a Reset writes too. #326 added a distinct
failure reason for exactly that, because the generic one misdirects the operator. Each scenario states
which route is reachable and asserts that the stated reason names a remedy that can work.

"Reachable" and "would succeed" are different claims. #326's own test asserts only that the admin
request reaches its handler rather than being answered by the health gate.

### 5. Make each scenario independent

**Status:** ⬜ Not started

Each creates, seeds, and destroys its own volume, and depends on no state another one left behind.

### 6. Remove the obsolete section and record why the technique stopped working

**Status:** ⬜ Not started

The note goes where the surviving `--read-only` discussion lives, so nobody re-derives the technique
from it. **The #294 test keeps `--read-only`** — it uses it as a restricted-write environment and
asserts success, which is the currently measured behaviour.

### 7. Assert no migration number or schema version anywhere

**Status:** ⬜ Not started

The suite's existing rule. Counts move whenever any milestone adds a migration, and a hardcoded number
goes stale on its own and gets "fixed" by editing the number rather than by anyone checking what
happened.

### 8. Extend `StartupResilienceTests` in-process

**Status:** ⬜ Not started

For the sabotage techniques that are deterministic and cross-platform. Additional to the container
scenarios, never a replacement — the environmental tier is what proves the behaviour on a real mount.

Confirm each named test is achievable via `WebApplicationFactory` before implementation starts; any
that is not drops to T2-only and is recorded as such here.

### 9. Fill in #339's template fields

**Status:** ⬜ Not started

Including Determinism — step 1's measurements are exactly what that field records — and the smoke-set
designation, proposed here for approval.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | The obsolete section and its `--read-only`-forces-a-failure technique are removed, with a note recording why it stopped working | Live | Section gone; note present beside the surviving `--read-only` discussion |
| 2 | ❌ | #294's test still uses `--read-only` and still asserts success | Live | Its document is unchanged by this issue |
| 3 | ❌ | Each scenario is a separate document in `startup-and-degradation/` | Live | Three documents, one per scenario |
| 4 | ❌ | Every degrading scenario asserts process alive, `/health` unhealthy, pages 200, OpenAPI reachable | Live | Each document's assertions cover all four |
| 5 | ❌ | Each scenario states the reachable recovery route and whether it can succeed | Live | Each document names the route and the remedy the reason gives |
| 6 | ❌ | Scenarios are independent — own volume, own seed, own teardown | Live | Running any one alone, in any order, produces the same result |
| 7 | ❌ | Unwritable-directory scenario reaches its failure state, with sidecar state pinned | Live | `:ro` mount, stated stop procedure, and a confirmed-failure check rather than an inferred one |
| 8 | ❌ | Corrupt/truncated database scenario reaches its failure state | Live | Confirmed degraded, not inferred from the recipe |
| 9 | ❌ | Overshoot scenario asserts healthy plus the overshoot notification, not the degraded contract | Live | `/health` 200; the notification is present |
| 10 | ❌ | No scenario asserts a migration number or schema version | Live | `grep` for version literals in the three documents returns nothing |
| 11 | ❌ | In-process cases extend `StartupResilienceTests`, red before green | Unit test | `Startup_DatabaseFileCorrupt_EntersDegradedStateInsteadOfCrashing`, `Startup_DatabaseFileCorrupt_HealthReportsUnhealthyRatherThanBeingUnreachable`, `Startup_SchemaVersionAheadOfApplication_StaysHealthyAndSurfacesTheOvershoot` |
| 12 | ❌ | Each document carries #339's template fields including Determinism and its smoke designation | Live | Field check against the template |
