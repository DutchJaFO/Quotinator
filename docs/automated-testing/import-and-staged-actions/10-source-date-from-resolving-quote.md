# A Source discovered from a quote carries that quote's date

**Smoke:** no
**Environment:** Fresh
**Traces to:** #191

## Preconditions

A Source discovered implicitly from a quote — no `sources[]` entry naming it — previously never carried
a date, even when the resolving quote had one.

> ### A gap recorded here was fixed, and this document said otherwise for three weeks
>
> From 2026-07-31 until 2026-08-22 this test carried a "known open gap, not yet filed as its own
> issue": that `PlanSourcesAsync` — the explicit `sources[]`-entry path — created a row with
> `Date = NULL` and no later quote ever backfilled it. It named `Frozen` and `Jurassic Park` as
> reproductions and told the reader to expect `NULL` for them.
>
> **Re-verified 2026-08-22 against a real database: both carry dates.** `Frozen` is `2013`,
> `Jurassic Park` is `1993`. Something between #191 and now closed it — `#190`'s Optional-aware
> `ResolveAgainst` is the most likely candidate — and nobody noticed, because the expectation was
> written as prose rather than run as a check.
>
> That is the more useful finding than the gap ever was: **a test that tells you to expect a failure
> will keep telling you that after the failure is gone.** An expectation stated as "do not be
> surprised" is not verified by anything.

## Determinism

- **Copy the `-wal` and `-shm` sidecars** alongside `quotinatordata.db`. `DatabaseInitializer` runs in
  WAL mode and SQLite does not auto-checkpoint recent writes back into the main file until the WAL
  passes its size threshold or every connection closes — and the app holds one open. A `.db`-only copy
  can silently omit real, committed data. **Confirmed live 2026-08-04**: a batch-links count read `3`
  instead of the correct `4` from a bare copy, and matched once the sidecars were included.
  `sqlite3` is not present in the image, so `PRAGMA wal_checkpoint` via `docker exec` is not an
  option — copying the sidecars is the only fix that does not need a Dockerfile change.
- **Assert the relationship, not the totals.** `have_date` must be non-zero and a large majority of
  `sources` — before the fix it was always `0`, which is the fact this establishes. The absolute
  figures are data and move with the dataset (`439/479` when #191 shipped, `423/461` on 2026-08-22);
  do not write either into the expectation. If the ratio drops sharply, investigate what changed.
- **A Source with no date is not necessarily a failure.** `vilaboim_movie-quotes.json` carries
  `"date": null` for every entry, and titles appearing only there — Citizen Kane, Annie Hall, Chinatown
  — have no date available in any bundled file. Check whether a date exists upstream before treating a
  `NULL` as a defect.
- The container is **stopped** before copying, so the file is not being written mid-copy.

## Steps

Run the **Fresh** profile first.

### 1. Read path — confirm the seeded date surfaces

This does not re-exercise the fix:

```bash
curl -s "http://localhost:8080/api/v1/quotes/search?q=Airplane&field=source"
```

**Expected:** items whose `date` is `"1980"`, not `null`. This Source already exists from seeding, so
the call confirms the read path only — it does not by itself re-exercise `ResolveSourceAsync`.

### 2. Count the Sources carrying a date, against a fresh seed

This is the actual code path:

```bash
docker stop -t 15 qt-env
docker cp qt-env:/data/quotinatordata.db .claude/temp/inspect-191.db
docker cp qt-env:/data/quotinatordata.db-wal .claude/temp/inspect-191.db-wal
docker cp qt-env:/data/quotinatordata.db-shm .claude/temp/inspect-191.db-shm
docker start qt-env
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db ".claude/temp/inspect-191.db" \
  --sql "SELECT COUNT(*) AS sources, SUM(CASE WHEN Date IS NOT NULL THEN 1 ELSE 0 END) AS have_date FROM Quotinator_Source WHERE IsDeleted = 0"
```

**Expected:** `have_date` is non-zero and a large majority of `sources`.

### 3. Cross-check `Airplane!` — a title with no `sources[]` entry

This is the implicit-discovery path this fixes:

```bash
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db ".claude/temp/inspect-191.db" \
  --sql "SELECT Title, Type, Date FROM Quotinator_Source WHERE Title = 'Airplane!' AND IsDeleted = 0"
```

**Expected:** `Airplane!` returns `Date = 1980` — the implicit-discovery path this fixed.

### 4. Cross-check `Jurassic Park` — a title with a date-less explicit entry

This is the gap described in Preconditions:

```bash
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db ".claude/temp/inspect-191.db" \
  --sql "SELECT Title, Type, Date FROM Quotinator_Source WHERE Title = 'Jurassic Park' AND IsDeleted = 0"
```

**Expected:** `Jurassic Park` returns `Date = 1993`. It was previously documented here as expected
`NULL`; see the note in Preconditions.

### 5. Cross-check `Frozen` — the other title the fixed gap named

```bash
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db ".claude/temp/inspect-191.db" \
  --sql "SELECT Title, Type, Date FROM Quotinator_Source WHERE Title = 'Frozen' AND IsDeleted = 0"
```

**Expected:** `Frozen` returns `Date = 2013`.

This step exists because the expectation did not, until 2026-08-23: the document asserted `Frozen`'s
date with no query for `Frozen` anywhere in it. That is the same fault its own Preconditions note
describes — an expectation written as prose is not verified by anything — reproduced one paragraph
below the warning about it.

## Observed effect

Measured 2026-08-22 on a real seeded database: `423` of `461` sources carried a date — recorded as an
observation, not as an expectation for the next run. The shortfall was titles present only in
`vilaboim_movie-quotes.json`, which supplies `"date": null` throughout, so there was no date anywhere
to inherit: missing upstream data rather than a defect in this path.

## Cleanup

```bash
rm -f .claude/temp/inspect-191.db .claude/temp/inspect-191.db-wal .claude/temp/inspect-191.db-shm
```
