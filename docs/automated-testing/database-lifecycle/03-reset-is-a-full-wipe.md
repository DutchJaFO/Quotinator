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
- **The `preserveSchemaVersion` check compares row *counts* before and after**, not absolute values.
  The point is that granular per-version history survives rather than collapsing to a single baseline
  row; the counts themselves move whenever a migration is added.

## Steps

### 1. Create this test's own environment

```bash
dotnet script scripts/testing/test-env.csx -- create --name qt-db-03 --port 18303
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Record the starting state

```bash
curl -s "http://localhost:18303/api/v1/version" | grep -o '"quotes":[0-9]*'
curl -s "http://localhost:18303/api/v1/admin/audit" | grep -o '"totalCount":[0-9]*'
```

**Expected:** `quotes` and the audit `totalCount` are both non-zero — a normal seeded install.

**On failure:** a zero here makes the whole test vacuous — asserting these become zero proves nothing
if they were already zero. Stop; this is a seeding problem, not a Reset result.

### 3. Reset the database

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: smoketest" \
  "http://localhost:18303/api/v1/admin/database/reset"
```

**Expected:** `200` with every row count `0`. No reimport happens.

### 4. Read the quote count after Reset

```bash
curl -s "http://localhost:18303/api/v1/version" | grep -o '"quotes":[0-9]*'
```

**Expected:** `/version`'s `quotes` count is `0`.

### 5. Read the audit count after Reset

```bash
curl -s "http://localhost:18303/api/v1/admin/audit" | grep -o '"totalCount":[0-9]*'
```

**Expected:** the audit `totalCount` is exactly `1` — Reset's own self-trace row. The audit trail is
wiped along with everything else, no longer surviving Reset the way it did before #156.

### 6. Confirm the empty database degrades rather than failing

```bash
curl -s -w " [%{http_code}]\n" "http://localhost:18303/api/v1/quotes/random"
```

**Expected:** `200` with `{"status":"NoResults", ...}` and an empty `items` array — not `503`, and not
real quote data.

### 7. Reset again with `preserveSchemaVersion=true`

This restores pre-reset migration history for both counters — #156 made this symmetric, since Data's
own `System_SchemaVersion` is wiped by the full drop too, where previously it was never touched.

**Both counts must be read before the `preserveSchemaVersion=true` call as well as after** — the
assertion is that they are unchanged, which cannot be evaluated from the after-value alone.

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: smoketest" \
  "http://localhost:18303/api/v1/admin/database/reset?preserveSchemaVersion=true"
```

**Expected:** `200`.

### 8. Read both schema-version counters

The container is stopped for the copy, and the `-wal`/`-shm` sidecars come with it — a copy taken
mid-write can be missing exactly what was just written, and reads as a wrong count rather than as an
error:

```bash
docker stop -t 15 qt-db-03
MSYS_NO_PATHCONV=1 docker cp qt-db-03:/data/quotinatordata.db .claude/temp/smoke156.db
MSYS_NO_PATHCONV=1 docker cp qt-db-03:/data/quotinatordata.db-wal .claude/temp/smoke156.db-wal
MSYS_NO_PATHCONV=1 docker cp qt-db-03:/data/quotinatordata.db-shm .claude/temp/smoke156.db-shm
docker start qt-db-03
until curl -sf http://localhost:18303/api/v1/health > /dev/null; do sleep 1; done
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke156.db \
  --sql "SELECT COUNT(*) AS DataVersions FROM System_SchemaVersion"
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke156.db \
  --sql "SELECT COUNT(*) AS ConsumerVersions FROM System_ConsumerSchemaVersion"
```

**Expected:** both counters report the same row count as before the `preserveSchemaVersion=true` call —
their granular per-version history, not collapsed to a single baseline row.

## Observed effect

Partially established. The row counts and the self-trace row are observed state and are asserted
above. What the container logs during the drop and rebuild has not been captured.

## Explicitly not covered here

There is no live check for the `SeedSystemContentAsync` extension point. No real system or reference
table exists in production yet — it is proven only via test-only fixtures, see #156's plan doc — so
nothing observable changes in a running container for that part.

## Cleanup

```bash
dotnet script scripts/testing/test-env.csx -- destroy --name qt-db-03
rm -f .claude/temp/smoke156.db .claude/temp/smoke156.db-wal .claude/temp/smoke156.db-shm
```
