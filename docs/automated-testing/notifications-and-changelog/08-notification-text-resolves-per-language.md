# A notification's title and body resolve to the requested language

**Smoke:** no
**Environment:** Fresh
**Traces to:** #319

## Preconditions

Nothing beyond the profile. The operation-id-rename announcement is written by a startup producer on
every healthy boot and carries translations of its own, so the subject exists without an import.

## Determinism

- **The subject is one notification identified by a known cause** — the announcement, selected by
  `metadataKind -eq 'announcement'`. No step counts notifications or reads `items[0]`: how many exist
  depends on which producers exist and what the changelog flags for the running version, and which one
  sorts first moves with them.
- **Presence is asserted per language, never a total.** The failure this guards is a join that excludes
  rows rather than falling back, which makes a notification vanish for a reader in an untranslated
  language. Selecting the same notification by `metadataKind` in each language detects that without
  depending on how many others are present.
- **Every `.Count` is taken through `@(…)`.** PowerShell 5.1 gives a single `PSCustomObject` no `Count`
  property, so an unwrapped count prints blank for exactly one match — the expected result here.
- **`pageSize=0` on every listing**, so the selection searches the whole set rather than the first page.
- **`?lang=` and `Accept-Language` are set explicitly in every request.** A request setting neither
  resolves against the container's own culture, and the result would depend on the host.
- **Comparing the Dutch body against the English one is what separates a translation from a labelled
  fallback.** Every field can read correctly while the join matched nothing and `COALESCE` fell through
  to the original with the `CASE` still reporting `nl`; no single field reveals that disagreement.
- **Never assert a migration number or schema version** — the tables arrive in whatever migration the
  current build assigns them.

## Steps

### 1. Start a fresh container

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-08 --port 18508 --image quotinator:local
dotnet script scripts/testing/http.csx -- --url "http://localhost:18508/api/v1/health" --wait-for 200 --status
```

**Expected:** `200`.

**On failure:** the producers have not run, so the subject does not exist. Stop.

### 2. The announcement resolves in its original language

```powershell
$base = "http://localhost:18508/api/v1"
function Get-Announcement($lang) {
  @((Invoke-RestMethod "$base/notifications?pageSize=0&lang=$lang").items |
    Where-Object { $_.metadataKind -eq 'announcement' })[0]
}
$en = Get-Announcement 'en'
"present=$($null -ne $en) lang=$($en.language -eq 'en') original=$($en.originalLanguage -eq 'en') " +
"notTranslated=$($en.isTranslated -eq $false) hasBody=$(-not [string]::IsNullOrWhiteSpace($en.body))"
```

**Expected:** every value `True`.

**On failure:** `present=False` means the join dropped the notification the producer wrote; `original=False`
means the language column was never populated and every fallback below resolves to nothing. Stop either way.

### 3. An untranslated language falls back rather than disappearing

```powershell
$fr = Get-Announcement 'fr'
"present=$($null -ne $fr) reportsOriginal=$($fr.language -eq 'en') " +
"notTranslated=$($fr.isTranslated -eq $false) sameBody=$($fr.body -eq $en.body)"
```

**Expected:** every value `True`.

**On failure:** `present=False` means a reader in an untranslated language sees no notification at all —
the join is excluding rows instead of falling back, which is a harder failure than a missing translation.

### 4. A translated language returns different text

```powershell
$nl = Get-Announcement 'nl'
"present=$($null -ne $nl) lang=$($nl.language -eq 'nl') original=$($nl.originalLanguage -eq 'en') " +
"translated=$($nl.isTranslated -eq $true) bodyDiffers=$($nl.body -ne $en.body) " +
"titleDiffers=$($nl.title -ne $en.title)"
```

**Expected:** every value `True`.

**On failure:** `translated=False` means no translation row was written; `bodyDiffers=False` with
`translated=True` means the projection's two halves disagree — see `Determinism`.

### 5. `?lang=` outranks `Accept-Language`

```powershell
$h = @{ 'Accept-Language' = 'de' }
$withLang = @((Invoke-RestMethod "$base/notifications?pageSize=0&lang=nl" -Headers $h).items |
               Where-Object { $_.metadataKind -eq 'announcement' })[0].language
$header   = @((Invoke-RestMethod "$base/notifications?pageSize=0" -Headers $h).items |
               Where-Object { $_.metadataKind -eq 'announcement' })[0].language
"langWins=$($withLang -eq 'nl') headerUsed=$($header -eq 'de')"
```

**Expected:** both `True`. Either alone would hold if the other input were ignored entirely.

### 6. A malformed language is rejected

```powershell
dotnet script scripts/testing/http.csx -- --url "$base/notifications?lang=not-a-language" --expect 400
```

**Expected:** exit code `0`.

**On failure:** a `200` means the value reached the SQL comparison unvalidated; a `500` means it reached
it and threw.

### 7. The dismiss endpoint resolves the same way

```powershell
$id = $en.id
$id
$dismissed = Invoke-RestMethod -Method Post -Uri "$base/notifications/$id/dismiss?lang=nl" `
  -Headers @{'X-Api-Key' = 'smoketest'}
"lang=$($dismissed.language -eq 'nl') translated=$($dismissed.isTranslated -eq $true) " +
"dismissed=$($dismissed.isDismissed -eq $true) bodyDiffers=$($dismissed.body -ne $en.body)"
```

**Expected:** `$id` prints a non-blank id, then every value `True`.

**On failure:** an empty `$id` produces a request to `…/notifications//dismiss`, which fails for an
unrelated reason.

## Observed effect

A notification stores its text once, in the language its producer wrote it in, and the API renders that
text into whichever language a caller asks for. A caller asking for a language the notification has no
translation for receives the original text and is told so — `language` reports `en`, `isTranslated`
reports `false` — rather than receiving an empty body or a language label that does not match the words.
The Blazor surfaces make the same request using the interface's own culture, so a user reading the site
in Dutch sees Dutch notification text with English as the visible fallback.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-08
```
