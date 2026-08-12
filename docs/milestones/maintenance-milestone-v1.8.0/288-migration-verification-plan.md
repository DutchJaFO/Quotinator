# #288 — Migration review: verify full incremental path from last-shipped v1.8.2 schema

**Status:** Released
**GitHub issue:** #288
**Tiers required:** N/A (pure verification, no code change — see Steps for the live Docker verification performed in place of T1/T2)
**Depends on:** none

---

## Background

Per [ADR 009](../architecture-decisions/009-verify-migrations-against-last-released-schema.md), every
migration a milestone adds must be verified against a database matching the schema of the last actual
published release before the milestone is considered ready to close. This is tracked as its own issue
per milestone — #155 was this check for the "Data Import & Sources" milestone; #288 is this check for
the current v1.8.0 milestone (#18).

**Verified before starting:** the last real release is v1.8.2, tagged 2026-07-31
(`git log -1 --format=%ai v1.8.2`). Since then, this milestone added 2 new Consumer-owned migrations
(`Migration005_ImportBatchConflictPolicyCheckConstraint` from #150, `Migration006_DomainPrefixRename`
from #253/#254) and 6 new Data-owned migrations (versions 3-8, from #150, #253/#254, #251, #252, #280)
— confirmed by diffing `QuotinatorMigrations.cs`/`DatabaseInitializer.cs` between the v1.8.2 tag and
`HEAD`. None of these have shipped in a real release.

## Approach

Unlike #155, this check found no bug and required no code change — the process below only exercises
the already-existing migration path. `ghcr.io/dutchjafo/quotinator:1.8.2` is a real, published Docker
image, so it stands in for "a genuine v1.8.2 installation" more faithfully than reconstructing one from
a git worktree (#155's technique, used because no such published image existed for the SQLite-era
`v1.7.2` release it verified against).

## Steps

### 1. Seed a real v1.8.2 database via the published image

**Status:** Done, 2026-08-10.

`docker pull ghcr.io/dutchjafo/quotinator:1.8.2` (already the currently-tagged latest release), then
ran it with a fresh bind-mounted data volume (`Quotinator__DataDir=/data`). **Used PowerShell, not Git
Bash**, for the `docker run -e ... /data` invocation — Git Bash's MSYS layer silently mangles a leading
`/data` in an env var's value into a Windows path (`C:/Program Files/Git/data`), which was caught live
(first attempt logged `Data: C:/Program Files/Git/data` and left the bind-mounted host directory empty)
before being redone correctly.

Result: a genuine v1.8.2 database — `schema v4 (data v2)`, 799 quotes / 461 sources / 12 characters /
3 people / 30 series / 7 universes / 2 stage directions / 1 sound cue / 4 conversations — confirmed live
via `GET /api/v1/quotes/random`, then the container was stopped.

### 2. Run the current branch's image against that same database

**Status:** Done, 2026-08-10.

Built `quotinator:migration-check` from the current branch's `docker/Dockerfile`, then ran it against
the same bind-mounted volume the v1.8.2 container had just seeded. Startup log:

```
[Database - Backup] backing up v4 → "/data/backups/quotinatordata_v4_20260810T201630Z.db"
[Database - Init] applying 6 pending "Data" migration(s) (version 2 → 8)...
[Database - Init] applying 2 pending "App" migration(s) (version 4 → 6)...
[Database - Init] schema updated (data v8, app v6)
[Database - Stats] 799 quotes  461 sources  12 characters  3 people  30 series  7 universes  2 stage directions  1 sound cues  4 conversations
```

No exception. Every row count identical before and after — the upgrade neither lost nor duplicated any
data. The automatic pre-migration backup (`backing up v4 → ...`) ran as designed.

### 3. Confirm functional correctness against the upgraded database, not just schema shape

**Status:** Done, 2026-08-10.

Exercised live endpoints against the upgraded database, deliberately targeting code paths this
milestone touched (#281's `CompletenessStatus` fallback, #284's `JoinQueryRepository`-based reference
readers, #285's `IJoinStrategy`-based conversation line lookups, #287's async `IQuoteService`):

| Endpoint | Result |
|---|---|
| `GET /api/v1/version` | `schemaVersion: 6`, all 10 stats fields correct |
| `GET /api/v1/masterdata/sources` | 461 total, item shape correct |
| `GET /api/v1/masterdata/people` | 3 total, `completenessStatus: "NeedsReview"` present (not null — #281) |
| `GET /api/v1/masterdata/characters` | 12 total, `sources[]` reference array populated (#284) |
| `GET /api/v1/quotes/{id}` | resolves correctly via the async `GetById` path (#287) |
| `GET /api/v1/conversations/{id}` | 5-line conversation resolves with `series`/`universe` references populated per line (#284/#285/#287 combined) |

All correct. Containers and images (`quotinator-182-seed`, `quotinator-migration-check`,
`quotinator:migration-check`) were removed after the check.

### 4. Confirm no already-released migration was edited in place

**Status:** Done, 2026-08-10.

Byte-for-byte diff (not `git log -p` by eye) of every migration constant that shipped in v1.8.2,
comparing the v1.8.2 tag against `HEAD`:

| Constant | Result |
|---|---|
| `Migration001_InitialSchema` | Identical |
| `Migration002_ReseedGenres` | Identical |
| `Migration003_ImportBatches` | Identical |
| `Migration004_ConsolidatedSinceV172Core` | Identical |
| `CharacterGlobalIdentityMerge` | Identical |
| `AuditMigrations.CreateAuditEntriesTable` (Data v1) | Identical |
| `DataConsolidatedMigrations.SinceV172` (Data v2) | Identical |

No post-release edits found — this milestone has not repeated the #56/#155-class incident.

### 5. Confirm the from-empty schema-drift tests still agree

**Status:** Done, 2026-08-10.

`Baseline_And_IncrementalReplay_ProduceIdenticalConsumerSchema` +
`Baseline_And_IncrementalReplay_AcceptSameCheckConstraintValues` (`Quotinator.Core.Tests`) and the 13
`DataOwnedBaseline_And_IncrementalReplay_*` tests (`Quotinator.Data.Tests`) all pass — the from-release
result (step 2) and the from-empty result (these tests) agree on the same final schema.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | A database snapshot matching the real v1.8.2 schema was used, not an accumulated local database | Live | `docker pull ghcr.io/dutchjafo/quotinator:1.8.2` + fresh seed, confirmed `schema v4 (data v2)` via `/api/v1/version` |
| 2 | ✅ | Every migration this milestone added applies cleanly, in order, against that snapshot | Live | Current-branch image startup log: `applying 6 pending "Data" migration(s) (version 2 → 8)`, `applying 2 pending "App" migration(s) (version 4 → 6)`, no exception |
| 3 | ✅ | The from-release result matches what the from-empty incremental-replay and baseline paths already produce | Unit test | `Baseline_And_IncrementalReplay_*` (Core.Tests, 2/2) + `DataOwnedBaseline_And_IncrementalReplay_*` (Data.Tests, 13/13) all pass |
| 4 | ✅ | No migration that already reached v1.8.2 was edited in place since | Live (review) | Byte-for-byte diff of all 7 already-shipped migration constants, v1.8.2 tag vs. `HEAD` — all identical |
| 5 | ✅ | The upgraded database is functionally correct, not just schema-shape-correct | Live | 6 endpoint calls against the upgraded database, covering #281/#284/#285/#287's own code paths — all correct |
| 6 | ✅ | Data survives the upgrade intact | Live | Row counts identical before/after: 799 quotes, 461 sources, 12 characters, 3 people, 30 series, 7 universes, 2 stage directions, 1 sound cue, 4 conversations |
| 7 | ✅ | Findings summarised in a closing comment | Live | Posted to #288 |

---

## Notes

No bug was found — unlike #155, which surfaced a genuine legacy-`SchemaVersion` upgrade bug during its
own equivalent check. This milestone's migration path upgrades cleanly from the last real release with
no code change required. Migration *minimization* (whether these 2 Consumer + 6 Data migrations should
be squashed into fewer, following #155's precedent) is deliberately out of scope for this issue and
tracked separately, since squashing is a code change with its own risk profile distinct from verifying
the path that already exists.
