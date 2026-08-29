# A notification's title and body resolve to the requested language

**Smoke:** no
**Environment:** Fresh
**Traces to:** #319

## Preconditions

A fresh container on the current build. The bundled startup producers write at least one notification
with translations of their own, so nothing has to be imported to make this observable.

The behaviour under test is a *read-time* resolution: one stored notification, several languages out.
That is why every step below reads the same notification back rather than creating a new one per
language — a test that wrote one notification per language would pass even if resolution never
happened.

## Determinism

- **Which notification is present depends on what the producers wrote**, so no step names a specific
  title or body string. Each step reads `language`, `originalLanguage` and `isTranslated` off whatever
  notification the list returns first, and compares *those* — the identity of the notification is not
  the subject.
- **`?lang=` and `Accept-Language` are pinned separately in every request.** A step that set neither
  would resolve against the container's own culture, which is the host's, and the result would depend
  on the machine running it.
- **A language with no translation is the point of step 3, not a failure of it.** The fallback is the
  contract; a run that cannot reach an untranslated language has not tested the fallback and fails.
- **Never assert a specific migration number or schema version** — the tables arrive in whatever
  migration the current build assigns them.

## Steps

### 1. Start a fresh container

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-08 --port 18508 --image quotinator:local
```

**Expected:** reaches `Quotinator ready`.

**On failure:** nothing below can run. Stop.

### 2. Read a notification in its original language

```powershell
(Invoke-RestMethod "http://localhost:18508/api/v1/notifications?lang=en").items[0] |
  Select-Object language, originalLanguage, isTranslated, title, body | Format-List
```

**Expected:** `language` is `en`, `originalLanguage` is `en`, and `isTranslated` is `False`.

`title` and `body` are non-empty. An empty `body` here means the read projection's `COALESCE` returned
nothing for a notification that definitely has text, which is the failure mode the fallback exists to
prevent.

**On failure:** if `originalLanguage` is empty rather than `en`, the column was never populated — every
subsequent fallback resolves to nothing, so the remaining steps would report an unrelated symptom. Stop.

### 3. Read the same notification in a language it has no translation for

```powershell
(Invoke-RestMethod "http://localhost:18508/api/v1/notifications?lang=fr").items[0] |
  Select-Object language, originalLanguage, isTranslated, body | Format-List
```

**Expected:** `language` is `en` — **not** `fr` — `isTranslated` is `False`, and `body` is the same
text step 2 returned.

**The response must not report `language: fr`.** Echoing the requested language back for text that was
not translated is the specific defect this contract prevents: it makes an untranslated notification
indistinguishable from a translated one to any client that trusts the field.

**On failure:** a `body` that is empty or null means the fallback did not fire at all, and the notification
would render blank to every reader outside its original language. That is cause 1 — fix the code.

### 4. Read it in a language it does have a translation for

```powershell
$nl = (Invoke-RestMethod "http://localhost:18508/api/v1/notifications?lang=nl").items[0]
$en = (Invoke-RestMethod "http://localhost:18508/api/v1/notifications?lang=en").items[0]
$nl | Select-Object language, originalLanguage, isTranslated | Format-List
"same body as English: $($nl.body -eq $en.body)"
```

**Expected:** `language` is `nl`, `originalLanguage` is `en`, `isTranslated` is `True`, and the
comparison prints `False` — the Dutch body genuinely differs from the English one.

**`same body as English: True` is a failure even though every field looks right.** It means the join
matched no translation row and the `COALESCE` fell through to the original while the `CASE` still
reported `nl` — the two halves of the projection disagreeing, which no single field can reveal on its
own. This comparison is the only step that can tell a real translation from a fallback wearing its
label.

**On failure:** if `isTranslated` is `False`, no producer wrote a translation for Dutch. Check whether
the startup producers ran at all (step 1's log) before concluding the read path is at fault — a missing
row and a broken join look identical from here.

### 5. Confirm `?lang=` outranks `Accept-Language`

```powershell
$h = @{ "Accept-Language" = "de" }
(Invoke-RestMethod "http://localhost:18508/api/v1/notifications?lang=nl" -Headers $h).items[0].language
(Invoke-RestMethod "http://localhost:18508/api/v1/notifications" -Headers $h).items[0].language
```

**Expected:** the first prints `nl`, the second prints `de`.

Both are needed. The first alone would pass if `Accept-Language` were ignored entirely; the second
alone would pass if `?lang=` were. Together they establish the precedence rather than either half of it.

**On failure:** if both print the same value, one of the two inputs is not reaching the reader.

### 6. Confirm a malformed language is rejected

```powershell
try { Invoke-RestMethod "http://localhost:18508/api/v1/notifications?lang=not-a-language" }
catch { $_.Exception.Response.StatusCode.value__ }
```

**Expected:** `400`, matching what `/api/v1/quotes` returns for the same input.

**On failure:** a `200` means the value reached the SQL comparison unvalidated. A `500` means it reached
it and threw.

### 7. Confirm the dismiss endpoint resolves the same way

```powershell
$id = (Invoke-RestMethod "http://localhost:18508/api/v1/notifications?lang=en").items[0].id
$k  = @{ "X-Api-Key" = "<admin key>" }
(Invoke-RestMethod -Method Post -Headers $k `
  "http://localhost:18508/api/v1/notifications/$id/dismiss?lang=nl") |
  Select-Object language, isTranslated, isDismissed | Format-List
```

**Expected:** `language` is `nl`, `isTranslated` is `True`, `isDismissed` is `True`.

The dismiss endpoint echoes the notification back, so it resolves text through the same projection. A
`language` of `en` here means it does not, and a caller dismissing in Dutch would receive English.

### 8. Tear down

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-08
```

**Expected:** the container is removed and the command reports no error.
