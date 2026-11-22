# Each applied migration says what it did, one notification per migration

**Smoke:** no
**Environment:** Upgraded
**Deterministic:** yes
**Traces to:** #371

## Preconditions

**Beyond the profile.** One container, `qt-notif-16` on `18516`, run on two images against the same
volume: `ghcr.io/dutchjafo/quotinator:1.8.3` first, which creates and seeds a released-shape database,
then this build, which migrates it.

**The previous published tag is the prior image, deliberately**, not the milestone base image. This is
the upgrade users are about to perform, and it is the only arrangement that shows a description
resolved by a *newer* build for a migration an *older* one applied, which is the property requirement 3
rests on: nothing about a migration's description is persisted.

The companion case, a database created from scratch, cannot share a database with this one and has its
own document: [*A created database says so*](15-a-created-database-says-so.md).

## Determinism

**The expected count is derived from this same run, never written down.** The application logs
`applying {Count} pending {Phase} migration(s)` once per phase, and the number the notifications are
compared against is the sum of those `Count` values read from this container's own log. A figure
written into this document would go stale the moment any milestone adds a migration, and would then be
"fixed" by editing the digit rather than by anyone checking what happened. For the same reason **no
step names a migration number or a phase's starting version**: see the index's *Never assert a specific
migration number or schema version*.

**No step asserts a total notification count.** The upgrade also writes what's-new notifications and
carries 1.8.3's own announcement forward. Every assertion here counts occurrences of the one kind this
document is about.

**Three numbers are read together, because the count alone cannot fail usefully.** A correct run and a
run that resolved every description to the same fallback produce the same count; only the number of
*distinct* bodies separates them, and only a check for empty bodies catches a description that resolved
to nothing.

**`databasecreated` must be absent, and that is correct rather than a miss.** The 1.8.3 container
created this database, and 1.8.3 has no such producer.

**Read the log before every stop**, per the index's *Read the log before the application stops*:
`reenter` replaces the container, so step 1 reads its `[Runtime - Exception]` count before step 2
re-enters, and step 2 counts only what was logged since.

## Steps

### 1. Create a released-shape database on the previous published tag

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-notif-16 --create-directory C:\repos\Quotinator\.claude\temp\qt-notif-16
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-16 --port 18516 --image ghcr.io/dutchjafo/quotinator:1.8.3
dotnet script scripts/testing/http.csx -- --url "http://localhost:18516/api/v1/quotes?pageSize=1" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-notif-16\quotes.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-16\quotes.json --greater-than totalCount=0
dotnet script scripts/testing/http.csx -- --url "http://localhost:18516/api/v1/notifications?pageSize=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-notif-16\released.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-16\released.json --where items:metadataKind=migrationapplied --length-equals items=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-16\released.json --where items:metadataKind=databasecreated --length-equals items=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-16 --pattern "\[Runtime - Exception\]" --exactly 0
```

**Expected:** every command exits `0`. The readiness poll inside `create` establishes that 1.8.3 started
healthy; the quote count is greater than zero, so this is a database with content to migrate rather than
an empty one; neither `migrationapplied` nor `databasecreated` appears, because neither producer exists
in that release; and nothing was thrown.

**On failure:** if 1.8.3 cannot be pulled, stop and report it rather than substituting another tag.
The index's *Users skip versions* records that an unpullable tag is the trigger to revisit the approach,
not a reason to quietly change what the test upgrades from.

### 2. Upgrade to this build, and compare the notifications against the log

The expectation is derived from this container's own log rather than written down: `--sum-out` adds up the
counts the log's `applying {Count} pending {Phase} migration(s)` lines report, and saves the total for the
`json` commands after it.

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-16 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- reenter --name qt-notif-16 --port 18516 --image quotinator:local
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-16 --pattern "applying (\d+) pending" --sum-out C:\repos\Quotinator\.claude\temp\qt-notif-16\applied.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-16\applied.json --greater-than sum=0
dotnet script scripts/testing/http.csx -- --url "http://localhost:18516/api/v1/notifications?pageSize=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-notif-16\upgraded.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-16\upgraded.json --where items:metadataKind=migrationapplied --length-equals-json items=C:\repos\Quotinator\.claude\temp\qt-notif-16\applied.json#sum --distinct-length-equals-json items:body=C:\repos\Quotinator\.claude\temp\qt-notif-16\applied.json#sum
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-16\upgraded.json --where items:metadataKind=migrationapplied --where items:body= --length-equals items=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-16\upgraded.json --where items:metadataKind=migrationapplied --where items:metadata.phase=App --present items.0
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-16\upgraded.json --where items:metadataKind=migrationapplied --where items:metadata.phase=Data --present items.0
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-16\upgraded.json --where items:metadataKind=databasecreated --length-equals items=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-16 --pattern "\[Runtime - Exception\]" --exactly 0
```

**Expected:** every command exits `0`. Nothing thrown before the stop; the log's sum is greater than zero;
the `migrationapplied` notifications number exactly that sum, one per migration, and their bodies take that
same number of distinct values, so no two say the same thing; none has an empty body, so every one names
what its migration did rather than only its version; both `App` and `Data` appear among the phases,
because this upgrade spans both lists; `databasecreated` does not appear; and nothing was thrown since
the upgrade.

**On failure:** a matching count with fewer distinct bodies than it means descriptions are resolving to
the wrong migration or to a shared fallback, which the count alone cannot distinguish from a correct run.
A count of `0` against a non-zero sum means the producer did not run at all. A sum of `0` means the log
line's wording changed, and every comparison after it would be against nothing.

### 3. Confirm the rows render on both surfaces, and carry no detail table

Both surfaces are read by the same committed page script,
[`notification-rows-by-kind.js`](../../../scripts/testing/page-scripts/notification-rows-by-kind.js), which
pairs each rendered row with its kind through the API, on the page or in the startup popup.

```
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18516/notifications --out C:\repos\Quotinator\.claude\temp\qt-notif-16\migrationapplied-page.png --width 1280 --height 1400 --script-file scripts/testing/page-scripts/notification-rows-by-kind.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-16\page-by-kind.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-16\page-by-kind.json --greater-than innerHeight=0 --equals-json byKind.migrationapplied.rows=C:\repos\Quotinator\.claude\temp\qt-notif-16\applied.json#sum --equals byKind.migrationapplied.emptyTitle=0 --equals byKind.migrationapplied.opensDetail=0 --equals byKind.migrationapplied.expandsInPlace=0
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18516/ --out C:\repos\Quotinator\.claude\temp\qt-notif-16\migrationapplied-popup.png --width 1000 --height 1400 --script-file scripts/testing/page-scripts/notification-rows-by-kind.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-16\popup-by-kind.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-16\popup-by-kind.json --greater-than innerHeight=0 --greater-than byKind.migrationapplied.rows=0 --equals byKind.migrationapplied.opensDetail=0 --equals byKind.migrationapplied.expandsInPlace=0
```

**Expected:** both `json` commands exit `0`. On the page, the rows of this kind number step 2's sum, none
with an empty title, and none opens a detail dialog or expands one in place, because this kind declares no
detail table. On the popup, rows of this kind are present, with the same two zeros. Both report a viewport
with a height, or the reading was taken against a pane with none and says nothing (see the index's *Does
the browser actually have a viewport?*).

**On failure:** fewer rows than step 2's sum means the page is not showing every row the API
returns, which is a paging or rendering defect rather than a producer one. An `opensDetail` above `0`
contradicts `NotificationTableTests.EveryMetadataKind_WithItsOwnPayload_RendersWhatItDeclares`, and one
of the two is wrong.

### 4. Restart again, and confirm no migration is announced twice

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-16 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- reenter --name qt-notif-16 --port 18516 --image quotinator:local
dotnet script scripts/testing/http.csx -- --url "http://localhost:18516/api/v1/notifications?pageSize=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-notif-16\again.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-16\again.json --where items:metadataKind=migrationapplied --length-equals-json items=C:\repos\Quotinator\.claude\temp\qt-notif-16\applied.json#sum
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-16 --pattern "\[Runtime - Exception\]" --exactly 0
```

**Expected:** every command exits `0`: nothing thrown before the stop, and the `migrationapplied` count is
step 2's sum, unchanged. Nothing was pending this time, so nothing was applied and nothing is reported;
and even had the producer run, each migration's identity is its phase and version, so none of them could
be written twice. Nothing was thrown by the replaced container.

**On failure:** a higher count is a dedupe failure. A lower one means notification rows are being
removed by a restart, which nothing in this path should do.

## Observed effect

**Measured 2026-11-22** against `quotinator:local` built from the working tree on `c683381c`, in this form,
every command run as written.

1.8.3 started healthy with `799` quotes and wrote one notification of its own, carrying no
`metadataKind` at all (that release predates typed metadata). Upgrading to this build, the log's two
`applying … pending` lines summed to `31`; the `migrationapplied` notifications numbered `31`, with `31`
distinct bodies, none empty, across both the `App` and `Data` phases. Both surfaces rendered all `31` with
no empty title, no detail dialog and no in-place detail, each against a `1400` viewport. A second restart
left the count at `31`. `databasecreated` never appeared, as 1.8.3 created that database. Nothing was
thrown at any read.

The counts are observed output, not assertions: the expectation is the log's own sum, and it moves
whenever a migration is added.

**Measured 2026-10-11**, in the PowerShell form this replaced: the same readings, at `29` migrations.

**Against the pre-issue build the same upgrade read `migrations this run says it applied: 28` and
`migrationapplied notifications: 0`.** The derived expectation being non-zero there is what makes this
document's own comparison meaningful: a broken log pattern would have produced `0` against `0` and
passed while measuring nothing. The count differing between the two runs, 28 then 29, is this build's
own new migration, and the expectation tracked it without the document being edited.

## Canary

Build the commit before [#371](https://github.com/DutchJaFO/Quotinator/issues/371)'s first code
change, under a distinct tag, and run steps 2 to 4 against it. Before #371 neither the migration
record nor `System_SchemaVersion` could say anything beyond the fact that a numbered migration had
applied, so step 2 finds no notification naming what any migration did.

**Step 2 compares the notifications against the log rather than against a number this document
states.** Migration counts move whenever any milestone adds one and are consolidated before a release,
so the assertion is that every migration the log reports applying has a notification saying what it
did, derived in the same run.

Steps 2 and 4 are the pair: announced once per migration, and not again on a restart with nothing left
to apply. Step 3's absence of a detail table is stated alongside what the row does render.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-16 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-16
docker image rm ghcr.io/dutchjafo/quotinator:1.8.3
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-notif-16
```

**Expected:** the `log` command exits `0`, nothing thrown before the stop. The named volume goes with the
container, so no database survives this document. The pulled 1.8.3 image is removed because nothing else
in the suite depends on it being present rather than pullable, and the output directory is removed.
