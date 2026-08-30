# A reset recommends a reseed, and running the recommendation resolves it

**Smoke:** no
**Environment:** Fresh
**Traces to:** #304

## Preconditions

**Beyond the profile.** One container of this test's own, `qt-notif-10`, publishing `19510`, on the
current build. Reset and reseed both require `X-Api-Key`, so the container is created with
`--env Quotinator__AdminApiKey=t2-304` and every admin call below sends that header.

Reset rebuilds the schema and deliberately does not reimport content (#156). Before #304 nothing told
the operator the database was now empty. This proves the recommendation appears, that running it puts
content back, and that it then stops being active.

## Determinism

**The seed must have finished before the reset runs.** A reset against a half-seeded database still
produces an empty database, so the test would pass for the wrong reason — it would never have
established that content was there to lose. The wait polls the quote count for a non-zero value rather
than sleeping for a duration.

**Count this recommendation, never the total number of notifications.** How many notifications exist
depends on which producers are present and what the bundled changelog flags for the running version,
both of which move every milestone. Every count here filters on `metadataKind` of `reseedRecommended`.

**Count only *active* recommendations, not every row carrying that kind.** `GET /notifications` returns
the full history, dismissed rows included — resolving the condition dismisses the row, it does not
delete it. A count that ignores `isDismissed` therefore never falls back to zero, and step 3 fails
against a working application. Found on this document's first run, where the reseed had correctly
dismissed the row (`isDismissed: true`) while the count still read `1`.

**Count objects, not matching lines.** These responses are single-line JSON, so a line-based match
reports `1` however many copies exist. `@(...).Count` over parsed items cannot disagree with the
response about what a match is.

**Re-wrap in `@(...)` at the call site, not only inside the helper.** PowerShell 5.1 unrolls a
single-element array on return, so `$rec = Get-ReseedRecommendations` yields a bare `PSCustomObject`
with no `Count` property, and `$rec.Count` prints empty — for exactly one match, which is this test's
expected result. Measured here on the first run: step 2 reported an empty count while the notification
was present and correct.

**Every count in this document is wrapped, not just the ones observed failing.** The zero case prints
`0` from the unwrapped form, so three of the four sites looked fine while being equally broken — step 4
then reported empty on the first clean run, after step 2 had already been fixed in isolation. The rule
is the wrap, not the site.

**Quote counts are read, never predicted.** The number of bundled quotes changes when a source file is
updated; what this test asserts is that the count is non-zero before the reset, zero after it, and
non-zero again after the reseed — facts the operation itself establishes.

**The count comes from `/quotes`' own `totalCount`, not from `/health`.** `/health` reports
`{"status":"healthy"}` and nothing else, so a gate written against a quote count there is never
satisfied — and it does not fail, it hangs, which is the failure mode
[`04`](04-upgrade-does-not-duplicate-the-legacy-notification.md) already records for a gate that cannot
become true. Found here the same way, while first running this document.

## Steps

### 1. Seed a fresh database and record what is in it

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-10 --port 19510 `
  --image quotinator:local --env Quotinator__AdminApiKey=t2-304

function Get-QuoteCount { (Invoke-RestMethod "http://localhost:19510/api/v1/quotes?page=1&pageSize=1").totalCount }
function Get-ReseedRecommendations {
  $items = (Invoke-RestMethod "http://localhost:19510/api/v1/notifications?pageSize=0").items
  @($items | Where-Object { $_.metadataKind -eq 'reseedRecommended' -and -not $_.isDismissed })
}

while ((Get-QuoteCount) -lt 1) { Start-Sleep 2 }
"quotes seeded = $(Get-QuoteCount)"
"recommendations before = $(@(Get-ReseedRecommendations).Count)"
```

**Expected:** a non-zero `quotes seeded`, and `recommendations before = 0`. Nothing has recommended a
reseed, because nothing has emptied the database.

**On failure:** a non-zero count here means a recommendation exists before this test caused one, so
step 2 cannot attribute what it sees to the reset. Stop.

### 2. Reset, and confirm it recommends rather than reseeds

```powershell
$headers = @{ "X-Api-Key" = "t2-304" }
Invoke-RestMethod -Method Post -Headers $headers `
  "http://localhost:19510/api/v1/admin/database/reset?allowNoBackup=true" | Out-Null

"quotes after reset = $(Get-QuoteCount)"
$rec = @(Get-ReseedRecommendations)
"recommendations after reset = $($rec.Count)"
$rec | Select-Object -First 1 | ForEach-Object { "type=$($_.type) title=$($_.title)" }
```

**Expected:** `quotes after reset = 0`, `recommendations after reset = 1`, and that one row reporting
`type=actionrequired` (the API serializes the type in lower case) with a non-empty title.

Both halves matter. A zero quote count alone would also be true if Reset had reseeded and failed; a
recommendation alone would not prove Reset left the database empty. Together they are the condition
#304 exists to surface — and the recommendation is the whole point, because Reset must not reseed on
the caller's behalf.

### 3. Run the recommendation, and confirm it resolves the condition

```powershell
Invoke-RestMethod -Method Post -Headers $headers `
  "http://localhost:19510/api/v1/admin/database/reseed" | Out-Null

"quotes after reseed = $(Get-QuoteCount)"
"recommendations after reseed = $(@(Get-ReseedRecommendations).Count)"
```

**Expected:** a non-zero `quotes after reseed`, and `recommendations after reseed = 0`.

This is the positive control the failure above needs: it proves the remedy the notification names
actually resolves the condition, rather than being advice nobody checked. It also proves the dismiss is
wired to the plain endpoint and not only to the notification action — an undismissed recommendation
would stay active and silently suppress every later occurrence.

### 4. Confirm the resolved recommendation records that it was done, not declined

```powershell
$all = (Invoke-RestMethod "http://localhost:19510/api/v1/notifications?pageSize=0").items
@($all | Where-Object { $_.metadataKind -eq 'reseedRecommended' }) |
  ForEach-Object { "isDismissed=$($_.isDismissed) dismissReason=$($_.dismissReason)" }
```

**Expected:** `isDismissed=True dismissReason=resolved`.

A notification stops being active for two very different reasons — the user set it aside, or the thing
it described was actually dealt with — and before #304 both were stored as nothing but `IsDismissed`.
The page therefore told a user who had just run the reseed that they had dismissed it. `resolved` here
is what the *Done* label is rendered from; `dismissed` would mean the reason was never recorded and the
display is back to guessing.

### 5. Reset again, and confirm the resolved condition recommends afresh

```powershell
Invoke-RestMethod -Method Post -Headers $headers `
  "http://localhost:19510/api/v1/admin/database/reset?allowNoBackup=true" | Out-Null

"recommendations after second reset = $(@(Get-ReseedRecommendations).Count)"
```

**Expected:** `1`.

The recommendation describes a condition that can recur, not an event that happened once. A dedupe
comparing against dismissed rows would report `0` here and leave the operator with an empty database
and no notice — which is the defect the active-only dedupe exists to prevent, and the only step in this
document that can catch it.

## Observed effect

Executed 2026-08-30 against `quotinator:local` — the values below come from a clean run of the document
exactly as written, on a fresh container, after the corrections above:

| Step | Observed |
|---|---|
| 1 | `quotes seeded = 799`, `recommendations before = 0` |
| 2 | `quotes after reset = 0`, `recommendations after reset = 1`, `type=actionrequired`, title `The database holds no quotes` |
| 3 | `quotes after reseed = 799`, `recommendations after reseed = 0` |
| 4 | `isDismissed=True dismissReason=resolved` |
| 5 | `recommendations after second reset = 1` |

Step 5 is the load-bearing observation: steps 2 and 3 would both pass against a full-history dedupe, and
only the second reset distinguishes them.

**Proven red before it was proven green.** The same steps were run against a build from the commit
immediately before #304's first feature commit (`46577c38`, via a throwaway worktree and a
`quotinator:pre304` image), and the document fails there exactly where it should: the reset empties the
database — `quotes after reset = 0` — and **`recommendations after reset = 0`** where step 2 requires
`1`. Step 4's field does not exist on that build at all. Both were then torn down (container, image,
worktree). Without this the document proves only that something happens, not that it would have caught
the absence it was written for.

A detail worth recording for whoever reads the row counts: after step 4 the *total* number of rows
carrying this kind is `1`, not `2`. Reset drops and rebuilds `System_Notification` with every other
table, so the dismissed row from step 3 does not survive it. That is consistent with Reset being a full
wipe, and it means this document cannot use "a dismissed row is still present" as evidence of anything
after a reset.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-10
```
