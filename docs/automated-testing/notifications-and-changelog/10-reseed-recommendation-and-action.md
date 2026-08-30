# A reset recommends a reseed, and running the recommendation resolves it

**Smoke:** no
**Environment:** Fresh
**Traces to:** #304

## Preconditions

**Beyond the profile.** One container of this test's own, `qt-notif-10`, publishing `19510`, on the
current build. Reset needs an admin key, so the container is created with one.

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

**Count objects, not matching lines.** These responses are single-line JSON, so a line-based match
reports `1` however many copies exist. `@(...).Count` over parsed items cannot disagree with the
response about what a match is.

**Quote counts are read, never predicted.** The number of bundled quotes changes when a source file is
updated; what this test asserts is that the count is non-zero before the reset, zero after it, and
non-zero again after the reseed — facts the operation itself establishes.

## Steps

### 1. Seed a fresh database and record what is in it

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-10 --port 19510 `
  --image quotinator:local --admin-key t2-304

function Get-QuoteCount { (Invoke-RestMethod "http://localhost:19510/api/v1/health").quotes }
function Get-ReseedRecommendations {
  $items = (Invoke-RestMethod "http://localhost:19510/api/v1/notifications?pageSize=0").items
  @($items | Where-Object { $_.metadataKind -eq 'reseedRecommended' })
}

while ((Get-QuoteCount) -lt 1) { Start-Sleep 2 }
"quotes seeded = $(Get-QuoteCount)"
"recommendations before = $((Get-ReseedRecommendations).Count)"
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
$rec = Get-ReseedRecommendations
"recommendations after reset = $($rec.Count)"
$rec | Select-Object -First 1 | ForEach-Object { "type=$($_.type) title=$($_.title)" }
```

**Expected:** `quotes after reset = 0`, `recommendations after reset = 1`, and that one row reporting
`type=ActionRequired` with a non-empty title.

Both halves matter. A zero quote count alone would also be true if Reset had reseeded and failed; a
recommendation alone would not prove Reset left the database empty. Together they are the condition
#304 exists to surface — and the recommendation is the whole point, because Reset must not reseed on
the caller's behalf.

### 3. Run the recommendation, and confirm it resolves the condition

```powershell
Invoke-RestMethod -Method Post -Headers $headers `
  "http://localhost:19510/api/v1/admin/database/reseed" | Out-Null

"quotes after reseed = $(Get-QuoteCount)"
"recommendations after reseed = $((Get-ReseedRecommendations).Count)"
```

**Expected:** a non-zero `quotes after reseed`, and `recommendations after reseed = 0`.

This is the positive control the failure above needs: it proves the remedy the notification names
actually resolves the condition, rather than being advice nobody checked. It also proves the dismiss is
wired to the plain endpoint and not only to the notification action — an undismissed recommendation
would stay active and silently suppress every later occurrence.

### 4. Reset again, and confirm the resolved condition recommends afresh

```powershell
Invoke-RestMethod -Method Post -Headers $headers `
  "http://localhost:19510/api/v1/admin/database/reset?allowNoBackup=true" | Out-Null

"recommendations after second reset = $((Get-ReseedRecommendations).Count)"
```

**Expected:** `1`.

The recommendation describes a condition that can recur, not an event that happened once. A dedupe
comparing against dismissed rows would report `0` here and leave the operator with an empty database
and no notice — which is the defect the active-only dedupe exists to prevent, and the only step in this
document that can catch it.

## Observed effect

Not yet established as a captured record. Step 4 is the load-bearing observation: steps 2 and 3 would
both pass against a full-history dedupe, and only the second reset distinguishes them.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-10
```
