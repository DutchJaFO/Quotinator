# The changelog lives in its own on-disk database and survives a restart

**Smoke:** yes
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #309

## Preconditions

**Beyond the profile.** One container of this test's own (`qt-notif-07`), on a bind-mounted directory rather than the profile's
named volume, so the host can see the database files directly, and with `Quotinator__IncludeDefaultSources=false`, since no quote
content plays any part here. It is restarted once, in place.

The changelog database is a file beside the main database. What this verifies is that **file-backed storage is what ships**, and that
the page reading from it keeps answering across a restart. Its content is the changelog JSON shipped in the image, which is the
application's own and not a bundled quote source, and nothing here asserts what it says.

## Determinism

**This document reads files and responses, never log wording.** The regression it guards was an in-memory database, and a file on disk
is what an in-memory database cannot leave. Nothing here waits on a duration or on a log line.

## Explicitly not covered here

**Which source served a read.** The About page renders identically from the database and from the JSON fallback, so no response can tell
them apart, and that is proven in-process instead:

- `ChangelogReaderTests.GetDocumentAsync_DatabasePopulated_ReturnsReassembledContent` reads with an empty fallback, so its content can only
  have come from the database, and `GetDocumentAsync_TablesMissing_FallsBackToFileService` is the other outcome.
- `ChangelogDatabaseWiringTests.ChangelogDatabase_IsNotAnInMemoryDatabase` and
  `.ChangelogDatabase_IsAFileNamedAlongsideTheMainDatabase` assert the real registration.
- `ChangelogSystemContentImporterTests.RefreshAsync_RunTwice_OverwritesNotDuplicates` covers the import repeating at every startup without
  duplicating rows.

## Steps

### 1. Start a container of this test's own on a bind-mounted directory

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-notif-07-data --delete C:\repos\Quotinator\.claude\temp\qt-notif-07-out --create-directory C:\repos\Quotinator\.claude\temp\qt-notif-07-data
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-07 --port 18507 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-notif-07-data --env Quotinator__IncludeDefaultSources=false
```

**Expected:** the app reaches healthy.

### 2. Confirm the changelog file exists alongside `quotinatordata.db`

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-not-empty C:\repos\Quotinator\.claude\temp\qt-notif-07-data\quotinatordata.db --expect-not-empty C:\repos\Quotinator\.claude\temp\qt-notif-07-data\quotinatorchangelog.db
```

**Expected:** both files hold content. An in-memory database leaves nothing on disk, and they are read from the host directory because
the bind mount is what makes the file's existence on real storage the thing being observed.

### 3. Confirm the page renders the changelog

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18507/about --accept text/html --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-notif-07-out\about.html
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-text C:\repos\Quotinator\.claude\temp\qt-notif-07-out\about.html "changelog-entry"
```

**Expected:** `/about` answers `200` and renders changelog entries. Changelog content is surfaced only on this page, so there is no REST
endpoint to read instead.

### 4. Confirm the file and the page survive a restart

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-07 --pattern "\[Runtime - Exception\]" --exactly 0
docker restart qt-notif-07
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-07 --mark C:\repos\Quotinator\.claude\temp\qt-notif-07-out\after-restart.mark
dotnet script scripts/testing/http.csx -- --url http://localhost:18507/api/v1/health --wait-for 200 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-not-empty C:\repos\Quotinator\.claude\temp\qt-notif-07-data\quotinatorchangelog.db
dotnet script scripts/testing/http.csx -- --url http://localhost:18507/about --accept text/html --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-notif-07-out\about-after-restart.html
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-text C:\repos\Quotinator\.claude\temp\qt-notif-07-out\about-after-restart.html "changelog-entry"
```

**Expected:** no exception before the restart, the file still holds content, and the page still renders entries.

### 5. Nothing unexpected was thrown since the restart

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-07 --since-mark C:\repos\Quotinator\.claude\temp\qt-notif-07-out\after-restart.mark --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18507/api/v1/import --file C:\repos\Quotinator\docs\automated-testing\startup-and-degradation\fixtures\malformed-import.json --duplicate-resolution review --expect 422 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-07 --since-mark C:\repos\Quotinator\.claude\temp\qt-notif-07-out\after-restart.mark --pattern "thrown: JsonReaderException" --at-least 1
```

**Expected:** no exception since the restart. The malformed upload then answers `422` and its `JsonReaderException` is logged, which is
the positive control: the same channel that read zero can see an exception, so the zero means something.

**On failure:** a count above zero is an issue, unless a Knowledgebase entry says otherwise. Report it with the line that names it.

## Observed effect

**Measured 2026-11-01** against `quotinator:local` built from `00a9dfe2`.

| Check | Reading |
|---|---|
| `quotinatorchangelog.db` after the first start | 1150976 bytes |
| `/about` after the first start | `200`, matches `changelog-entry` |
| `[Runtime - Exception]` before the restart | 0 |
| `quotinatorchangelog.db` after the restart | 1159168 bytes |
| `/about` after the restart | `200`, matches `changelog-entry` |
| `[Runtime - Exception]` since the restart | 0 |
| Malformed upload (the control) | `422`, `thrown: JsonReaderException` once |

The file's length is reported, not asserted: it follows the changelog's own content, and it grew by 8192 bytes across the restart without
that meaning anything this test relies on.

## Canary

Build the commit before [#309](https://github.com/DutchJaFO/Quotinator/issues/309)'s first code change, under a distinct tag, and run steps
1 and 2 against it. The changelog's own on-disk database is what #309 added, so step 2 finds no `quotinatorchangelog.db`.

Step 3 is expected to pass there, and it is the control: the page rendered from the JSON files before #309, so step 2's reading is about
the file rather than about a broken container.

**Measured 2026-11-01** on `quotinator:canary-309`, built from `13b8562e`, steps 1 to 3 in order. Step 2 fails: `quotinatordata.db`
holds content and `quotinatorchangelog.db` does not exist. Step 3 passes: `/about` answers `200` and matches `changelog-entry`.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-07 --bind
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-notif-07-data --delete C:\repos\Quotinator\.claude\temp\qt-notif-07-out
```

**Expected:** the container, its data directory and the output directory are gone.
