# Reset wipes the entire database and does not reseed

**Smoke:** yes
**Environment:** Fresh
**Traces to:** #156

## Preconditions

Nothing beyond the Fresh profile — its own first-boot seed is what produces the audit rows this test
reads, and those rows are what prove the wipe is total rather than selective. A container whose seeding
produced none would make half this test vacuous, which is why the non-zero check below runs before the
Reset rather than after.

**This test destroys the database it runs against.** That is its subject, not a side effect.

Reset drops the *entire* database — there is no `System_`/`Import_`/`Audit_` protected-table concept —
and rebuilds via the baseline path, reversing #141's preserve-on-reset behaviour.

## Determinism

- **Waits for health, not a duration.**
- **Both pre-Reset counts must be non-zero** before the Reset call. Asserting they become zero proves
  nothing if they were already zero.
- **The post-Reset audit count is exactly `1`, not `0`.** Reset writes its own self-trace row
  (`Operation: Reset`) into the freshly-rebuilt `Audit_Entry` table immediately after wiping it — the
  same pattern `DELETE /admin/audit` uses for its `Purged` trace. Expecting `0` here is the easy
  mistake and would report a false failure.
- **The `preserveSchemaVersion` check compares row *counts* before and after**, not absolute values;
  the counts themselves move whenever a migration is added.
- **Under Fresh that comparison is `1` against `1`, and cannot demonstrate preserved history.** A
  fresh database takes the consolidated baseline path and records one collapsed row per counter, so
  there is no granular history to preserve in the first place. Steps 7 and 8 say what they do and do
  not establish; proving the granular half needs the Upgraded profile.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-db-03 --port 18303
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Record the starting state

```powershell
$quotesBefore = (Invoke-RestMethod "http://localhost:18303/api/v1/version").database.quotes
$auditBefore  = (Invoke-RestMethod "http://localhost:18303/api/v1/admin/audit").totalCount
"quotes=$quotesBefore audit=$auditBefore"
```

**Expected:** both non-zero — a normal seeded install.

**On failure:** a zero here makes the whole test vacuous — asserting these become zero proves nothing
if they were already zero. Stop; this is a seeding problem, not a Reset result.

### 3. Reset the database

```powershell
dotnet script scripts/testing/http.csx -- --method POST `
  --url "http://localhost:18303/api/v1/admin/database/reset" --expect 200
```

**Expected:** `200` with every row count `0`. No reimport happens.

### 4. Read the quote count after Reset

```powershell
(Invoke-RestMethod "http://localhost:18303/api/v1/version").database.quotes
```

**Expected:** `0`.

### 5. Read the audit count after Reset

```powershell
(Invoke-RestMethod "http://localhost:18303/api/v1/admin/audit").totalCount
```

**Expected:** exactly `1` — Reset's own self-trace row. The audit trail is wiped along with everything
else, no longer surviving Reset the way it did before #156.

### 6. Confirm the empty database degrades rather than failing

```powershell
$random = dotnet script scripts/testing/http.csx -- --url "http://localhost:18303/api/v1/quotes/random" --expect 200 | ConvertFrom-Json
"status=$($random.status) items=$(@($random.items).Count)"
```

**Expected:** `200`, `status=NoResults` and `items=0` — not `503`, and not real quote data.

### 7. Reset again with `preserveSchemaVersion=true`

This restores pre-reset migration history for both counters — #156 made this symmetric, since Data's
own `System_SchemaVersion` is wiped by the full drop too, where previously it was never touched.

**Both counts must be read before the `preserveSchemaVersion=true` call as well as after** — the
assertion is that they are unchanged, which cannot be evaluated from the after-value alone.

```powershell
docker stop -t 15 qt-db-03
docker cp qt-db-03:/data/quotinatordata.db .claude/temp/smoke156-before.db
docker start qt-db-03
dotnet script scripts/testing/http.csx -- --url "http://localhost:18303/api/v1/health" --wait-for 200 --status

dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke156-before.db `
  --sql "SELECT COUNT(*) AS DataVersionsBefore FROM System_SchemaVersion"
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke156-before.db `
  --sql "SELECT COUNT(*) AS ConsumerVersionsBefore FROM System_ConsumerSchemaVersion"

dotnet script scripts/testing/http.csx -- --method POST `
  --url "http://localhost:18303/api/v1/admin/database/reset?preserveSchemaVersion=true" --expect 200 | Out-Null
```

**Expected:** `200`, and two before-counts recorded for step 8 to compare against.

**On a Fresh container both before-counts are `1`, and that makes steps 7 and 8 a weaker check than
they read as.** A database created fresh takes the one-step consolidated baseline path and records a
single collapsed row per counter, so there is no granular per-version history for `preserveSchemaVersion`
to preserve — `1` before and `1` after is satisfied just as well by a counter that was rebuilt from
scratch. Measured during #339's full run, including against a never-reset Fresh container, which also
reads `1` and `1`.

**Multi-row history only exists on a database that took the incremental path**, so proving the
granular half needs the **Upgraded** profile: seed from a published tag, let the current build replay
its migrations, and only then Reset with `preserveSchemaVersion=true`. That is a different environment
than this document declares and is not folded in here — what steps 7 and 8 establish under Fresh is
that the flag returns `200` and does not *reduce* either counter. Read them that way rather than as
proof that granular history survives.

### 8. Read both schema-version counters

The container is stopped for the copy, and the `-wal`/`-shm` sidecars come with it — a copy taken
mid-write can be missing exactly what was just written, and reads as a wrong count rather than as an
error. The two sidecar copies are allowed to fail: a checkpointed database has neither file, and that
is not an error:

```powershell
docker stop -t 15 qt-db-03
docker cp qt-db-03:/data/quotinatordata.db .claude/temp/smoke156.db
docker cp qt-db-03:/data/quotinatordata.db-wal .claude/temp/smoke156.db-wal 2>$null
docker cp qt-db-03:/data/quotinatordata.db-shm .claude/temp/smoke156.db-shm 2>$null
docker start qt-db-03
dotnet script scripts/testing/http.csx -- --url "http://localhost:18303/api/v1/health" --wait-for 200 --status

dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke156.db `
  --sql "SELECT COUNT(*) AS DataVersions FROM System_SchemaVersion"
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke156.db `
  --sql "SELECT COUNT(*) AS ConsumerVersions FROM System_ConsumerSchemaVersion"
```

**Expected:** both counters report the same row count as the before-readings step 7 recorded, and
neither is lower.

**Under Fresh both readings are `1`, which is the collapsed baseline row rather than preserved
history** — see step 7 for why, and for what this does and does not establish. A run wanting to prove
granular history survives has to start from an Upgraded database, where the counters hold more than
one row to begin with.

## Observed effect

Partially established. The row counts and the self-trace row are observed state and are asserted
above. What the container logs during the drop and rebuild has not been captured.

## Explicitly not covered here

There is no live check for the `SeedSystemContentAsync` extension point. No real system or reference
table exists in production yet — it is proven only via test-only fixtures, see #156's plan doc — so
nothing observable changes in a running container for that part.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-db-03
Remove-Item .claude/temp/smoke156.db, .claude/temp/smoke156.db-wal, .claude/temp/smoke156.db-shm, `
            .claude/temp/smoke156-before.db -ErrorAction SilentlyContinue
```
