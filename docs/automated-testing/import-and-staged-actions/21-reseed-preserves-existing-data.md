# A reseed imports the designated files and deletes nothing

**Smoke:** no
**Environment:** Fresh
**Traces to:** #372
**Fully green after:** #373 — steps 3, 4 and 6 pass once #372 lands; step 4's "changes nothing" reads
as a rewrite of every quote until #373 stops reporting identical content as modified

## Preconditions

**Beyond the profile.** One container of this test's own, `qt-reseed-21`, publishing `19522`, on the
current build, created with `--env Quotinator__AdminApiKey=t2-372`.

Reseed's one job is importing the designated files from the two origins. This proves it adds what is
missing, leaves alone what is already correct, raises a decision where content disagrees, and — the
part no unit test can settle — that data the operator added themselves survives a reseed.

## Determinism

**Row counts cannot tell the two behaviours apart, and this is the whole trap.** Every id in this
project is a hash of normalised content, so a truncate-then-reimport restores byte-identical rows for
everything the source files describe: same counts, same ids, same links. A test comparing any of them
passes just as happily against the deletion it exists to catch.

**Only content the files do not describe distinguishes them.** Step 2 writes one quote through the API
that appears in no bundled file. A reseed that deletes first loses it and cannot bring it back; a
reseed that only imports never touches it. Every assertion in this document that matters hangs off
that row, not off a total.

**`Environment: Fresh` is load-bearing.** The counts in step 1 are the baseline every later step is
measured against, and a reused container starts from an unknown one.

## Steps

### 1. Seed a fresh container and record the baseline

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-reseed-21 --port 19522 `
  --image quotinator:local --env Quotinator__AdminApiKey=t2-372

$headers = @{ "X-Api-Key" = "t2-372" }
function Get-QuoteCount { (Invoke-RestMethod "http://localhost:19522/api/v1/quotes?page=1&pageSize=1").totalCount }
function Get-PendingActions {
  @((Invoke-RestMethod "http://localhost:19522/api/v1/import/actions?pageSize=0" -Headers $headers).items |
    Where-Object { $_.status -eq 'Pending' })
}

while ((Get-QuoteCount) -lt 1) { Start-Sleep 2 }
$baseline = Get-QuoteCount
"baseline quotes = $baseline"
"baseline pending actions = $(@(Get-PendingActions).Count)"
```

**Expected:** a non-zero `baseline quotes`. Record the number; later steps compare against it rather
than against a literal, since bundled content changes over time.

### 2. Import a quote that no source file describes

**There is no `POST /quotes`** — the v1 API is read-only for quote content, and the first draft of this
document assumed otherwise. Running it returned `405`, which is what a canary is for. `POST /import` is
the supported write path, and it suits the fixture better anyway: the quote arrives through the same
pipeline a reseed uses, so nothing about it is special except that no *designated* file describes it.

```powershell
$fixture = Join-Path $env:TEMP "qt-reseed-21-local.json"
@'
{
  "quotes": [
    {
      "id": "9f3c1d2e-4a5b-4c6d-8e7f-0a1b2c3d4e5f",
      "quote": "Locally authored, present in no bundled file.",
      "originalLanguage": "en",
      "source": "Quotinator Test Harness",
      "date": "2026",
      "character": null,
      "author": null,
      "type": "movie",
      "genres": ["drama"],
      "translations": {}
    }
  ]
}
'@ | Set-Content -Path $fixture -Encoding utf8

$localId = "9f3c1d2e-4a5b-4c6d-8e7f-0a1b2c3d4e5f"
dotnet script scripts/testing/http.csx -- --url "http://localhost:19522/api/v1/import" `
  --method POST --api-key t2-372 --file $fixture
"quotes after import = $(Get-QuoteCount)"
```

**Expected:** an `imported: 1` summary, and `quotes after import` one greater than the baseline.

**Three shape requirements, each found by running this against a real container.** The wrapper object
(`{ "quotes": [ … ] }`) is required — a bare array returns `422`. `id`, `genres` and `translations` are
required by `schemas/source-flat.schema.json` even though the first draft omitted them. And the id is
stated rather than generated, so later steps can address the quote directly without a search.

**`http.csx`, not `Invoke-RestMethod -Form` and not `curl.exe`.** This project's shell is Windows
PowerShell 5.1, which has no `-Form` parameter — that is PowerShell 7+. The first draft reached for
`curl.exe`; the suite's own guard rejected it, and rightly, since `scripts/testing/http.csx` exists for
exactly this gap and keeps every document on one HTTP idiom.

**This row is the entire test.** If the import shape has changed and this step cannot land a quote,
stop — every assertion below becomes unfalsifiable, and a run that skips this step passes while proving
nothing.

### 3. Reseed, and confirm the local quote survives

```powershell
function Test-LocalQuote {
  # A statement-form try/catch, not an inline expression: Windows PowerShell 5.1 rejects
  # `$x = (try { … } catch { … })` outright with "The term 'try' is not recognized".
  try { Invoke-RestMethod "http://localhost:19522/api/v1/quotes/$localId" | Out-Null; return $true }
  catch { return $false }
}

Invoke-RestMethod -Method Post -Headers $headers `
  "http://localhost:19522/api/v1/admin/database/reseed" | Out-Null

"local quote survived reseed: $(Test-LocalQuote)"
"quotes after reseed = $(Get-QuoteCount)"
```

**Expected:** `local quote survived reseed: True`, and `quotes after reseed` still one greater than the
baseline.

**On failure:** if the quote is gone, the reseed is still deleting before it imports — the defect this
issue exists to remove. The row count alone will look correct in that case, because the bundled content
is restored identically; only the missing local quote reveals it.

### 4. Confirm a second reseed changes nothing

```powershell
$before = Get-QuoteCount
Invoke-RestMethod -Method Post -Headers $headers `
  "http://localhost:19522/api/v1/admin/database/reseed" | Out-Null
"quotes before = $before, after = $(Get-QuoteCount)"
"pending actions = $(@(Get-PendingActions).Count)"
```

**Expected:** the two counts equal, and no new pending actions.

**This is what "reseed is just an import" means in practice.** Nothing is missing and nothing disagrees,
so there is nothing to do. A reseed that reported work here would be re-adding content it had just
removed.

**Then confirm the report says so, rather than claiming a rewrite (#373).**

```powershell
$reports = (Invoke-RestMethod -Method Post -Headers $headers `
  "http://localhost:19522/api/v1/admin/database/reseed").reports
foreach ($r in $reports) {
  "$($r.fileName):"
  $r.entityTypes.PSObject.Properties | ForEach-Object {
    $c = $_.Value
    "   $($_.Name)  incoming=$($c.incoming) new=$($c.new) modified=$($c.modified) unchanged=$($c.unchanged)"
  }
}
```

**Expected:** every entity type the file contains is listed — not `Quote` alone — with `incoming`
matching the number that arrived, `unchanged` accounting for them, and `modified` at `0`.

**Two failures hide here, and the second is the one that costs time.** A quote already stored is
reported as *modified*, which claims a write that never happened. Every other entity type produces no
action at all and vanishes from the report entirely — so a reader comparing this against the cold start
sees seven entity types become one, and goes looking for a bug in entity handling that does not exist.

### 5. Remove a bundled quote and confirm the reseed repairs only that

```powershell
$victim = (Invoke-RestMethod "http://localhost:19522/api/v1/quotes?page=1&pageSize=50" -Headers $headers).items |
          Where-Object { $_.id -ne $localId } | Select-Object -First 1
Invoke-RestMethod -Method Delete -Headers $headers "http://localhost:19522/api/v1/quotes/$($victim.id)" | Out-Null
"quotes after delete = $(Get-QuoteCount)"

Invoke-RestMethod -Method Post -Headers $headers `
  "http://localhost:19522/api/v1/admin/database/reseed" | Out-Null
"quotes after repair reseed = $(Get-QuoteCount)"
"local quote still present: $(Test-LocalQuote)"
```

**Expected:** the count drops by one after the delete, returns to its previous value after the reseed,
and `local quote still present: True`.

**Both halves are required.** A reseed that wiped everything and reimported would also land on the
right total — the local quote is what says the repair was surgical rather than wholesale.

**If the delete endpoint soft-deletes rather than removing the row**, the count may not drop. Record
what it actually does and treat a soft delete as this step's subject instead; do not force the
assertion.

### 6. Confirm import batches survive

```powershell
$batches = (Invoke-RestMethod "http://localhost:19522/api/v1/import/batches?pageSize=0" -Headers $headers).items
"batches after three reseeds = $(@($batches).Count)"
$batches | Select-Object -First 5 | ForEach-Object { "  $($_.id)  type=$($_.type)  status=$($_.status)" }
```

**Expected:** the batches from the first seed are still listed. A reseed that removed them would
invalidate every review alert naming one, which is what #303's alerts do.

## Canary — run red against the build before #372

Per `docs/testing-policy.md`'s *Red first applies to automated tests, not only unit tests*. Written at
step 1 of the issue, so `HEAD` was still the pre-work build and no worktree or second image was needed.

Run 2026-09-02 against `quotinator:local` at `3e9bb19c`, in container `qt-reseed-21`:

| Step | Assertion | Pre-work result |
|---|---|---|
| 3 | `local quote survived reseed: True` | **fails** — `False`; `799` quotes after the reseed, having been `800` before it, and `0` search hits |
| 6 | import batches survive | **fails** — `5` before, `4` after |

**Step 4's report assertions, run 2026-09-02 against the post-#372, pre-#373 build.** Both failures
visible in one output:

```
quotinator-curated.json:
   Quote  incoming=0 new=0 modified=13 unchanged=0
quotinator-series-universe.json:
   Source  incoming=0 new=0 modified=0 unchanged=0
NikhilNamal17_popular-movie-quotes.json:
   Quote  incoming=0 new=0 modified=707 unchanged=0
vilaboim_movie-quotes.json:
   Quote  incoming=0 new=0 modified=99 unchanged=0
```

`quotinator-curated.json` reported seven entity types on its cold start and reports one here — the
Characters, People, Sources, Conversations, StageDirections and SoundCues all arrived and matched, and
say nothing. The thirteen quotes that changed nothing are counted as modified. `incoming` and
`unchanged` read `0` throughout because #373 has not populated them yet.

**Steps 1, 2, 4 and 5 are not expected to fail on the pre-work build.** Step 1 establishes a baseline
and step 2 creates the fixture. Steps 4 and 5 assert counts, and truncate-and-reimport lands on exactly
the right totals — which is the whole reason step 3 exists and why its assertion hangs on a row the
files cannot re-create. A count is not evidence here; the locally authored quote is.

**Two draft defects were found by running it, both of which would have made the document unexecutable
rather than merely wrong.** `POST /quotes` does not exist — the v1 API is read-only for quote content —
and `Invoke-RestMethod -Form` is PowerShell 7+, unavailable in this project's 5.1 shell. Neither is
visible from reading; both surfaced on the first real run, which is the argument for writing the
document before the code rather than after.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-reseed-21
```
