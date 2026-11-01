# The changelog is served from its own on-disk database, not the JSON fallback

**Smoke:** yes
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #309

## Preconditions

**Beyond the profile.** One container of this test's own (`qt-notif-07`), on a bind-mounted directory rather than the profile's
named volume, so the host can see the database files directly, and with `Quotinator__IncludeDefaultSources=false`, since no quote
content plays any part here. It is restarted once, in place.

The changelog database is a file beside the main database. What this verifies is that **file-backed storage is what ships and what
serves reads**. Its content is the changelog JSON shipped in the image, which is the application's own and not a bundled quote
source, and nothing here asserts what it says.

## Determinism

**Assert on the positive line, never on the absence of the negative one.** The About page renders identically whichever source served
it, because the JSON fallback does its job, so an absent fallback warning proves nothing alone. Only a positive *"the database
answered"* statement separates a silently fallen-back read from a healthy one, so the log is read here because the log is the surface:
there is no REST endpoint for the changelog.

**The importer and reader counts must match, and neither is predicted**, since the changelog grows with every release. They report the
same unit deliberately, so a read reporting fewer than the import wrote means it was served a partial or stale copy.

**Waits for a condition, never a guessed delay.** Health answers before the post-restart import has logged, so step 5 waits for the line
itself, with a bound that only stops a run that will never see it. What guards the in-memory regression instantly is that the file exists
on disk, and `ChangelogDatabaseWiringTests.ChangelogDatabase_IsNotAnInMemoryDatabase` and
`.ChangelogDatabase_IsAFileNamedAlongsideTheMainDatabase` assert the real DI registration.

## Steps

### 1. Start a container of this test's own on a bind-mounted directory

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-notif-07-data --delete C:\repos\Quotinator\.claude\temp\qt-notif-07-out --create-directory C:\repos\Quotinator\.claude\temp\qt-notif-07-data
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-07 --port 18507 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-notif-07-data --env Quotinator__IncludeDefaultSources=false
```

**Expected:** the app reaches healthy, having initialised and imported the changelog during startup.

### 2. Confirm the changelog file exists alongside `quotinatordata.db`

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-not-empty C:\repos\Quotinator\.claude\temp\qt-notif-07-data\quotinatordata.db --expect-not-empty C:\repos\Quotinator\.claude\temp\qt-notif-07-data\quotinatorchangelog.db
```

**Expected:** both files hold content. An in-memory database leaves nothing on disk, and they are read from the host directory because
the bind mount is what makes the file's existence on real storage the thing being observed.

### 3. Confirm the database-backed read path is in use, not the fallback

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-07 --pattern "\[Changelog - Import\] refreshed \d+ entries across 3 language" --at-least 1
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-07 --pattern "\[Changelog - Read\] served \d+ entries from the database" --at-least 1
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-07 --same-number "\[Changelog - Import\] refreshed (\d+) entries" "\[Changelog - Read\] served (\d+) entries"
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-07 --pattern "falling back to the JSON-backed changelog service" --exactly 0
```

**Expected:** the import wrote across three languages, the shipped set, the database itself answered a read, and the two report the same
number. No fallback was logged, which is the weaker half and why the positive lines come first.

### 4. Confirm a real page request is served from the database

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-07 --mark C:\repos\Quotinator\.claude\temp\qt-notif-07-out\before-about.mark
dotnet script scripts/testing/http.csx -- --url http://localhost:18507/about --accept text/html --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-notif-07-out\about.html
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-text C:\repos\Quotinator\.claude\temp\qt-notif-07-out\about.html "changelog-entry"
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-07 --since-mark C:\repos\Quotinator\.claude\temp\qt-notif-07-out\before-about.mark --pattern "\[Changelog - Read\] served \d+ entries from the database" --at-least 1
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-07 --pattern "falling back to the JSON-backed changelog service" --exactly 0
```

**Expected:** `/about` answers `200` and renders changelog entries, and that request logged a read from the database, which is what ties
the page to the database rather than to the fallback. Changelog content is surfaced only on this page.

### 5. Confirm the file survives a restart with its content intact

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-07 --pattern "\[Runtime - Exception\]" --exactly 0
docker restart qt-notif-07
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-07 --mark C:\repos\Quotinator\.claude\temp\qt-notif-07-out\after-restart.mark
dotnet script scripts/testing/http.csx -- --url http://localhost:18507/api/v1/health --wait-for 200 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-07 --pattern "\[Changelog - Import\] refreshed \d+ entries" --at-least 2 --wait-seconds 60
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-07 --same-number "\[Changelog - Import\] refreshed (\d+) entries" "\[Changelog - Read\] served (\d+) entries"
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-not-empty C:\repos\Quotinator\.claude\temp\qt-notif-07-data\quotinatorchangelog.db
```

**Expected:** no exception before the restart, the file still holds content, and the first start's and the restart's imports report the
same number as every read. It is rebuilt from the shipped JSON at every startup, so this confirms the rebuild does not duplicate rows.

**On failure:** one import line after 60 seconds means the restart's import never ran or never logged. A second number larger than the
first is the duplication this step exists for.

### 6. Nothing unexpected was thrown since the restart

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-07 --since-mark C:\repos\Quotinator\.claude\temp\qt-notif-07-out\after-restart.mark --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18507/api/v1/import --file C:\repos\Quotinator\docs\automated-testing\startup-and-degradation\fixtures\malformed-import.json --duplicate-resolution review --expect 422 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-07 --since-mark C:\repos\Quotinator\.claude\temp\qt-notif-07-out\after-restart.mark --pattern "thrown: JsonReaderException" --at-least 1
```

**Expected:** no exception since the restart. The malformed upload then answers `422` and its `JsonReaderException` is logged, which is
the positive control: the same channel that read zero can see an exception, so the zero means something.

**On failure:** a count above zero is an issue, unless a Knowledgebase entry says otherwise. Report it with the line that names it.

## Observed effect

**Measured 2026-11-01** against `quotinator:local` built from `00a9dfe2`. The entry count is this run's reading of the shipped changelog,
not an expectation.

| Reading | First start | After the restart |
|---|---|---|
| `quotinatorchangelog.db` on the host | 1150976 bytes | 1155072 bytes |
| `[Changelog - Import] refreshed N entries` | 126, across 3 languages | 126 |
| `[Changelog - Read] served N entries from the database` | 126, every read | 126, every read |
| `falling back to the JSON-backed changelog service` | none | not read |

`GET /about` answered `200` with changelog entries, and that request logged one read from the database.

**No exception was logged before the restart, or after it before the malformed upload.** That upload's `JsonReaderException` was then
logged once.

## Canary

Build the commit before [#309](https://github.com/DutchJaFO/Quotinator/issues/309)'s first code
change, under a distinct tag, and run steps 2 to 5 against it. The changelog's own on-disk database is
what #309 added, so step 2 finds no file beside `quotinatordata.db` and step 3 finds the JSON fallback
serving every read.

**Step 3 is why this document exists, and the regression it guards was silent and permanent once
triggered.** A page served correctly from the fallback looks exactly like a page served correctly from
the database, so step 4's rendered output cannot distinguish them and must not be relied on to. The
read path has to be read directly.

Step 5 is the other half: the file survives a restart with its content intact, which a fallback cannot
demonstrate at all.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-07 --bind
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-notif-07-data --delete C:\repos\Quotinator\.claude\temp\qt-notif-07-out
```

**Expected:** the container, its data directory and the output directory are gone.
