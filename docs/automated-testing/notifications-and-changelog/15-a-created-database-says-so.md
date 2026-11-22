# A database created from scratch says so, and a routine restart does not say it again

**Smoke:** no
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #371

## Preconditions

**Beyond the profile, nothing.** A first boot against an empty volume *is* the trigger: it is what
creates a database. This document imports no content of its own and introduces no defect.

The companion case, a migration applied to a database an earlier build created, cannot share a
database with this one and has its own document:
[*Each applied migration says what it did*](16-each-applied-migration-says-what-it-did.md). Creating a
database takes the baseline path, which applies no individual migration at all, so one container can
only ever show one of the two.

## Determinism

**No step asserts a total notification count.** How many notifications exist depends on which
producers exist and what the bundled changelog flags, both of which move every milestone. Every
assertion here counts occurrences of the one kind this document is about, which is the *cause* under
test.

**No step names a schema version.** The creation notification carries both of them, and a figure
written into this document would go stale the moment any milestone adds a migration, then get "fixed"
by editing the digit. What is asserted is that both fields are present and non-empty: see the index's
*Never assert a specific migration number or schema version*.

**`migrationapplied` must be absent throughout, and that is an assertion rather than an omission.** It
is what proves the baseline path reports no individual migration, which is the distinction the
companion document's count depends on.

**Read the log before every stop**, per the index's *Read the log before the application stops*:
`reenter` replaces the container, so step 1 reads its `[Runtime - Exception]` count before step 2
re-enters.

## Steps

### 1. Cold start on an empty volume, and confirm the creation notification

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-notif-15 --create-directory C:\repos\Quotinator\.claude\temp\qt-notif-15
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-15 --port 18515 --image quotinator:local
dotnet script scripts/testing/http.csx -- --url "http://localhost:18515/api/v1/notifications?pageSize=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-notif-15\first-start.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-15\first-start.json --where items:metadataKind=databasecreated --length-equals items=1 --not-empty items.0.title --not-empty items.0.body --not-empty items.0.metadata.dataSchemaVersion --not-empty items.0.metadata.appSchemaVersion
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-15\first-start.json --where items:metadataKind=migrationapplied --length-equals items=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-15 --pattern "\[Runtime - Exception\]" --exactly 0
```

**Expected:** every command exits `0`: exactly one `databasecreated` notification, with a non-empty title,
a non-empty body and both schema versions in its payload; no `migrationapplied` notification; and no
exception logged. The payload is a JSON string inside the response, which the path reads into.

**On failure:** `migrationapplied` present here means the baseline path reported migrations it never
applied. Stop, and report it against the companion document too: its count is derived on the assumption
that a created database contributes none.

### 2. Restart the same volume, and confirm it is not announced again

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-15 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- reenter --name qt-notif-15 --port 18515 --image quotinator:local
dotnet script scripts/testing/http.csx -- --url "http://localhost:18515/api/v1/notifications?pageSize=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-notif-15\restarted.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-15\restarted.json --where items:metadataKind=databasecreated --length-equals items=1
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-15\restarted.json --where items:metadataKind=migrationapplied --length-equals items=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-15 --pattern "\[Runtime - Exception\]" --exactly 0
```

**Expected:** every command exits `0`: nothing thrown before the stop; still exactly one `databasecreated`,
not two; still no `migrationapplied`; and nothing thrown by the replaced container, whose log starts with
its own start. This startup found an existing database with nothing pending, so it had neither event to
report.

**On failure:** `databasecreated=2` is a dedupe failure rather than a producer failure: the event is
being re-reported instead of re-detected. Either reading means a routine restart is not silent.

### 3. Confirm the row renders, and carries no action

Both page scripts are committed files:
[`notification-rows-by-kind.js`](../../../scripts/testing/page-scripts/notification-rows-by-kind.js), which
pairs each rendered row with its kind through the API, and
[`notification-row.js`](../../../scripts/testing/page-scripts/notification-row.js), which reads the row whose
title holds the text in the URL fragment.

```
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18515/notifications --out C:\repos\Quotinator\.claude\temp\qt-notif-15\databasecreated-page.png --width 1280 --height 1400 --script-file scripts/testing/page-scripts/notification-rows-by-kind.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-15\page-by-kind.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-15\page-by-kind.json --greater-than innerHeight=0 --equals byKind.databasecreated.rows=1 --equals byKind.databasecreated.emptyTitle=0 --equals byKind.databasecreated.titleInsideBody=0 --equals byKind.databasecreated.opensDetail=0 --equals byKind.databasecreated.extraButtons=
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18515/notifications#Database created" --out C:\repos\Quotinator\.claude\temp\qt-notif-15\databasecreated-row.png --width 1280 --height 1400 --script-file scripts/testing/page-scripts/notification-row.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-15\row.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-15\row.json --equals buttons=Dismiss
```

**Expected:** both `json` commands exit `0`. The viewport has a height, or the reading was taken against
a pane with none and says nothing (see the index's *Does the browser actually have a viewport?*). One row
of this kind renders; its title is non-empty and is its own element rather than repeated inside the body;
it opens no detail, because this kind declares no detail table; it carries no button beyond `Details` and
`Dismiss`; and its buttons are `Dismiss` alone, because nothing resolves the fact that a database was
created.

**On failure:** `opensDetail: true` contradicts
`NotificationTableTests.EveryMetadataKind_WithItsOwnPayload_RendersWhatItDeclares`, and one of the two
is wrong. An extra button means an action was wired to a notification that has nothing to act on.

## Observed effect

**Measured 2026-11-22** against `quotinator:local` built from the working tree on `c683381c`, in this form,
every command run as written.

A first boot on an empty volume wrote one `databasecreated` notification, titled *Database created*, its
body *No database was found, so a new one was created at data schema v30 and app schema v9. There was
nothing to preserve.*, and its payload carrying `dataSchemaVersion = 30` and `appSchemaVersion = 9`. No
`migrationapplied` notification was written. The restart left `databasecreated` at one. On the page, the
row rendered with its title as its own element, no detail control and `Dismiss` as its only button, in a
`974 x 83` rectangle against a `1400` viewport. Nothing was thrown at any read.

The version numbers are observed output, not assertions: they move whenever a migration is added.

**Measured 2026-10-11**, in the PowerShell form this replaced: the same readings, at data schema v28.
**Against the pre-issue build the same first boot read `announcement=1 reseedfileapplied=5
whatsnew=1`**, with `databasecreated` absent, and the page carried no such row to find. The same
browser instrument did locate the announcement row in that run (`control: true`, 7 rows), so the
absence was the notification's and not the selector's.

## Canary

Build the commit before [#371](https://github.com/DutchJaFO/Quotinator/issues/371)'s first code
change, under a distinct tag, and run steps 1 and 3 against it. A database created from scratch said
nothing before #371, so step 1 finds no creation notification and step 3 has no row to render.

**Steps 1 and 2 are a pair and neither is worth running alone.** Step 1 asserts the announcement
appears on a cold start, step 2 asserts the same volume restarted does not announce it again. A build
that announced on every boot satisfies step 1 and fails step 2; one that never announces fails step 1
and passes step 2. Only together do they describe the behaviour.

Step 3 also asserts an absence, that the row carries no action, so it states what the row does carry in
the same breath rather than counting buttons and finding none.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-15 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-15
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-notif-15
```

**Expected:** the `log` command exits `0`, nothing thrown before the stop. The container's named volume goes
with it, so no database survives this document, and the output directory is removed.
