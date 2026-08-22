# The changelog is served from its own on-disk database, not the JSON fallback

**Smoke:** yes
**Environment:** Fresh
**Traces to:** #309

## Preconditions

A container with a mapped data directory, so the database file can be inspected on disk.

The changelog database is a file beside the main database. What this verifies is that **file-backed
storage is what ships and what serves reads** — a feature, checkable immediately.

## Determinism

**Assert on the positive line, never on the absence of the negative one.** The About page renders
identically whichever source served it, because the JSON fallback does its job — which is exactly why
this defect survived a full T2 pass unnoticed. An absent fallback warning was originally treated as the
decisive signal, but absence proves nothing: until this was fixed, the empty-database fallback logged
nothing at all, so a silently-fallen-back read and a healthy one produced identical output. Only a
positive *"the database answered"* statement distinguishes them.

**The importer and reader counts must match.** They report the same unit deliberately, so
`refreshed N entries` and `served N entries` are directly comparable; a read reporting fewer than the
import wrote means it was served a partial or stale copy. **Compare the two numbers to each other —
neither is predicted**, since the changelog grows with every release.

> **A sixteen-minute wait used to sit here and was removed (developer direction, 2026-08-19.)** It slept
> past the one observed failure at +13 minutes and re-read. The reasoning was that no shorter check
> could see the defect — but the mechanism behind that 13 minutes was never established, because the fix
> removed the dependency rather than explaining the timer. **An interval with no basis in an understood
> mechanism tests nothing**: it is not derived, so a regression failing at 40 minutes would sail past it,
> and a green result buys confidence it has not earned. A test verifies a feature or a reliable
> behaviour, never a guessed delay.
>
> What guards this now is deterministic and instant: the file exists on disk, and
> `ChangelogDatabaseWiringTests.ChangelogDatabase_IsNotAnInMemoryDatabase` /
> `.ChangelogDatabase_IsAFileNamedAlongsideTheMainDatabase` assert the real DI registration is not an
> in-memory connection string. A file does not evaporate; an in-memory database is caught before it
> ships.

## Steps

```bash
MSYS_NO_PATHCONV=1 docker run -d --name qt-changelog -p 8080:8080 \
  -v /tmp/qt-changelog/data:/data \
  -e Quotinator__AdminApiKey=<your admin key> quotinator:local
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
```

**The file must exist alongside `quotinatordata.db`** — an in-memory database leaves nothing on disk:

```bash
docker exec qt-changelog sh -c "ls -l /data/quotinatorchangelog.db"
```

**Confirm the database-backed read path is in use, not the fallback:**

```bash
docker logs qt-changelog 2>&1 | grep -E "Changelog - (Init|Import|Read)"
```

**Confirm a real page request is served from the database:**

```bash
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:8080/about
curl -s http://localhost:8080/about | grep -oE "changelog-entry" | wc -l
docker logs qt-changelog 2>&1 | grep -c "entries from the database"
docker logs qt-changelog 2>&1 | grep -c "falling back to the JSON-backed changelog service"
```

**The file must survive a restart with its content intact** — it is rebuilt from the bundled JSON at
every startup, so this confirms the rebuild is idempotent rather than duplicating rows:

```bash
docker restart qt-changelog
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
docker exec qt-changelog sh -c "ls -l /data/quotinatorchangelog.db"
docker logs qt-changelog 2>&1 | tail -40 | grep -E "Changelog - (Init|Import)"
```

## Expected output

- `/data/quotinatorchangelog.db` exists.
- `[Changelog - Import] refreshed N entries across 3 language(s)` appears, and so does
  `[Changelog - Read] served N entries from the database` — the positive statement that the database
  itself answered. **The two counts match each other**; the value itself is data. The three languages
  are asserted, because that is the shipped set rather than a content count.
- No `falling back to the JSON-backed changelog service` line appears at any point.
- `/about` returns `200` and renders changelog entries. **There is deliberately no REST endpoint here**
  — changelog content is surfaced only on the About page (`Components/Pages/About.razor`), so that is
  what must be read.
- The `entries from the database` count **increased** as a result of that request; the fallback count is
  `0`.
- After restart, the file is still present and the import reports the same entry count — no duplication.

## Observed effect

Well established, and the failure is the instructive part. Thirteen minutes after a clean import of 126
entries, every read failed with `no such table: Changelog_Entry` and fell back to the JSON service
permanently, with no process restart in between — the changelog database was a shared-cache in-memory
instance held open by a keep-alive connection.

**Nothing was user-visible, because the JSON fallback works exactly as designed.** That is why it went
unnoticed, and why this test asserts a positive statement rather than an absent warning.

Neither Reset nor the pre-migration backup touches this file (developer decision, 2026-08-18): its
contents are wholly derived from JSON shipped in the image, so nothing user-authored is ever at risk.

## Cleanup

```bash
docker rm -f qt-changelog
rm -rf /tmp/qt-changelog
```
