# Reset wipes the entire database and does not reseed

**Smoke:** yes
**Environment:** Fresh
**Traces to:** #156

## Preconditions

A container seeded normally, with a non-empty audit trail — the audit rows are what prove the wipe is
total rather than selective, so a container whose seeding produced none makes half this test vacuous.

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

**Seed, record the starting state, Reset:**

```bash
docker rm -f smoke156
MSYS_NO_PATHCONV=1 docker run -d --name smoke156 -p 8080:8080 \
  -e Quotinator__AdminApiKey=<your admin key> quotinator:local
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
curl -s "http://localhost:8080/api/v1/version" | grep -o '"quotes":[0-9]*'
curl -s "http://localhost:8080/api/v1/admin/audit" | grep -o '"totalCount":[0-9]*'
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/admin/database/reset"
curl -s "http://localhost:8080/api/v1/version" | grep -o '"quotes":[0-9]*'
curl -s "http://localhost:8080/api/v1/admin/audit" | grep -o '"totalCount":[0-9]*'
curl -s -w " [%{http_code}]\n" "http://localhost:8080/api/v1/quotes/random"
```

**`preserveSchemaVersion=true` restores pre-reset migration history for both counters** — #156 made
this symmetric, since Data's own `System_SchemaVersion` is wiped by the full drop too, where
previously it was never touched:

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/admin/database/reset?preserveSchemaVersion=true"
```

Then, via `Quotinator.Tools.DbInspector`:

```sql
SELECT COUNT(*) FROM System_SchemaVersion;
SELECT COUNT(*) FROM System_ConsumerSchemaVersion;
```

## Expected output

- Before Reset: `quotes` and the audit `totalCount` are both non-zero — a normal seeded install.
- The Reset call returns `200` with every row count `0`. No reimport happens.
- After Reset: `/version`'s `quotes` count is `0`. The audit trail is wiped along with everything
  else, no longer surviving Reset the way it did before #156.
- The audit `totalCount` is exactly `1` — Reset's own self-trace row.
- `/quotes/random` returns `200` with `{"status":"NoResults", ...}` and an empty `items` array — not
  `503`, and not real quote data.
- `preserveSchemaVersion=true` returns `200`, and both counters report the same row count as before
  the call — their granular per-version history, not collapsed to a single baseline row.

## Observed effect

Partially established. The row counts and the self-trace row are observed state and are asserted
above. What the container logs during the drop and rebuild has not been captured.

## Explicitly not covered here

There is no live check for the `SeedSystemContentAsync` extension point. No real system or reference
table exists in production yet — it is proven only via test-only fixtures, see #156's plan doc — so
nothing observable changes in a running container for that part.

## Cleanup

```bash
docker rm -f smoke156
```
