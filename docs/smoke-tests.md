# Smoke Tests — Docker T2 Verification Checklist

This is the single authoritative T2 smoke-test suite, run against a locally built Docker image
before tagging a release or whenever a T2 verification pass is required (see
`docs/release-verification.md`'s T2 gate, which points here rather than keeping its own copy, to
avoid the two drifting apart). See `CLAUDE.md`'s Pre-Push Checklist step 6 for when this must be run.

**This is a living checklist**: whenever a T2 pass surfaces a new bug or edge case, add its
verification command here in the same commit that fixes it — the list only grows, never shrinks.

---

## Contents

1. [Baseline](#1-baseline--healthversionrandomsearch)
2. [Import and staged-action review workflow](#2-import-and-staged-action-review-workflow-45-149-152-154) (#45, #149, #152, #154)
3. [Two-phase decide→apply reversal](#3-two-phase-decideapply-reversal-177) (#177)
4. [`batchId`-mode alias](#4-batchid-mode-alias-154) (#154)
5. [Discard](#5-discard-154) (#154)
6. [Reverse (undo)](#6-reverse-undo-59) (#59)
7. [Bodyless request validation](#7-bodyless-request-validation-154) (#154)
8. [StageDirection/SoundCue Modify/decidability](#8-stagedirectionsoundcue-modifydecidability-171172) (#171/#172)
9. [Person: explicit id, Modify/decidability, dateOfBirth/dateOfDeath](#9-person-explicit-id-modifydecidability-dateofbirthdateofdeath-173) (#173)
10. [Series/Universe schema, Character↔Source many-to-many identity](#10-seriesuniverse-schema-charactersource-many-to-many-identity-179) (#179)
11. [Sources.Date populated from the resolving quote](#11-sourcesdate-populated-from-the-resolving-quote-191) (#191)
12. [Canonicalize explicit ids at capture](#12-canonicalize-explicit-ids-at-capture-sourcepersonstagedirectionsoundcueconversation-209) (#209)
13. [Pagination contract](#13-pagination-contract-pagesize0-max-500-default-20-page-beyond-last-195) (#195)
14. [Quotes.Id case-insensitive lookup](#14-quotesid-case-insensitive-lookup-210) (#210)
15. [ConversationLines.QuoteId FK safety](#15-conversationlinesquoteid-fk-safety-210s-casing-unification-revision) (#210 revision)
16. [Systemic id-case guard](#16-systemic-id-case-guard-210s-scope-expansion) (#210 scope expansion)
17. [Read-time presentation normalization for string-typed id-reference fields](#17-read-time-presentation-normalization-for-string-typed-id-reference-fields-210s-third-revision) (#210 third revision)
18. [Uniform SELECT-list wrapping via IEntityColumnMetadata](#18-uniform-select-list-wrapping-via-ientitycolumnmetadata-210s-follow-on-round) (#210 follow-on)
19. [`batchId` validated explicitly on apply/discard/reverse; request logging reports the real final status code](#19-batchid-validated-explicitly-on-actionsapply-actionsdiscard-actionsreverse-request-logging-reports-the-real-final-status-code)
20. [Character Modify/decidability via the widened `characters[]` schema](#20-character-modifydecidability-via-the-widened-characters-schema-case-insensitive-source-natural-key-matching-175) (#175)
21. [Bulk-decide a staged batch via file export/import — CSV and JSON](#21-bulk-decide-a-staged-batch-via-file-exportimport--csv-and-json-163) (#163)
22. [Per-source conflict-resolution rule files and title-alias files — fresh seed produces zero pending actions](#22-per-source-conflict-resolution-rule-files-and-title-alias-files-181--fresh-4-file-seed-produces-zero-pending-actions) (#181)
23. [Rule file live-read proof](#23-rule-file-live-read-proof-181) (#181)
24. [ConflictResolutionRule staleness → new Stale status](#24-conflictresolutionrule-staleness--new-stale-status-153) (#153)
25. [SourceAliasRule staleness](#25-sourcealiasrule-staleness-153) (#153)
26. [Rule-file override endpoints](#26-rule-file-override-endpoints-153) (#153)
27. [Per-file, per-entity-type import/seed report](#27-per-file-per-entity-type-importseed-report-221) (#221)

---

## 1. Baseline — health/version/random/search
```bash
docker run --rm -p 8080:8080 -e Quotinator__AdminApiKey=<your admin key> quotinator:local
curl -s http://localhost:8080/api/v1/health
curl -s http://localhost:8080/api/v1/version
curl -s http://localhost:8080/api/v1/quotes/random
curl -s "http://localhost:8080/api/v1/quotes/search?q=love"
curl -s "http://localhost:8080/api/v1/quotes/search?q=Casablanca&field=source"
curl -s "http://localhost:8080/api/v1/quotes/search?q=Churchill&field=author"
curl -s "http://localhost:8080/api/v1/quotes/search?q=Rick&field=character"
curl -s "http://localhost:8080/api/v1/quotes/search?q=love&type=person"
```
Check that `/version` returns the expected version number — a missing `Directory.Build.props` in the build context silently produces `1.0.0` while `/health` still returns healthy.
The search queries cover: default full-text (`love` should return results), `field=source` (`Casablanca` should return results), and `field=author` (`Churchill` should now return the curated Winston Churchill quote — see the curated `person`-type entries added below). `field=character` (`Rick`) and `type=person&q=love` may still return an empty `items` array with a `message`, since no bundled data currently matches either; that is expected behaviour, not a bug.

---

## 2. Import and staged-action review workflow (#45, #149, #152, #154)

Re-imports a bundled file with `review` policy forced, so the endpoint that would otherwise auto-resolve via the default policy instead produces a genuine pending action to exercise decide/undo/apply against. `/api/v1/import/actions/*` (#154's unified staging engine) is the live mechanism — every import and seed run stages through it now.
```bash
curl -s "http://localhost:8080/api/v1/import/actions"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import/conflicts"
```
The first call should return `200` with an empty or existing `items` list — proves the endpoint is reachable with no setup. The second call **must return `404`** — `/import/conflicts` was removed entirely in #154 Phase B; if this ever returns anything else again, the legacy manual-review machinery has regressed back in.
```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' \
  -w "\n%{http_code}\n" \
  "http://localhost:8080/api/v1/import"
curl -s "http://localhost:8080/api/v1/import/actions?status=pending"
```
The import itself should return `202` (not `200`) since the re-imported quotes are genuine duplicates left `Pending` under `review`. The `status=pending` filter is deliberately lowercase here, not `Pending` — proves the case-insensitive `status`/`entityType`/`batchId` query-filter fix (#154) is still in effect. After the import, this must show exactly the action(s) just created (with `ambiguousFields` populated only when the fields genuinely differ — re-importing the same file unmodified means they usually won't). From the response, copy one pending action's `id` and its `batchId`.
```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" -H "Content-Type: application/json" \
  -d '{"quoteText":{"choice":"keep"}}' \
  "http://localhost:8080/api/v1/import/actions/<id>/decide"
curl -s "http://localhost:8080/api/v1/import/actions?status=Decided"
curl -s -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/<id>/undo"
curl -s -X POST -H "X-Api-Key: <your admin key>" -H "Content-Type: application/json" \
  -d '{"quoteText":{"choice":"keep"}}' \
  "http://localhost:8080/api/v1/import/actions/<id>/decide"
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/apply?batchId=<lowercase the batchId here too>"
```
After `decide`, `status=Decided` must show it; after `undo`, it must be back under `status=Pending`; after `decide` again, ready to apply. **If the curated file's re-import produces more than one pending action** (it currently produces two — both `Airplane!` quotes), `apply` at this point correctly returns `422` with a `pendingActionIds` array listing the ones still undecided — this is the batch-apply-atomicity contract working as designed, not a bug. Decide each remaining id the same way, then re-run `apply` until it returns `200` and the quote's field reflects the decision. Applying with a deliberately lowercased `batchId` here also re-confirms the case-insensitive fix.

---

## 3. Two-phase decide→apply reversal (#177)

A batch applied entirely through the staged
review→decide→apply flow (i.e. via `POST /import/actions/apply` directly, not `POST /import`'s own
single-shot path) previously never had its own `ImportBatches.Status` set to `Applied`, so
`POST /import/actions/reverse` always rejected it with a bare `422` even though the batch had
genuinely applied. Re-import the curated file under `review` again and decide every pending action
from the sequence above (repeat the `decide` call for each remaining `id` until none are left
pending), then:
```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/apply?batchId=<batchId>"
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/reverse?batchId=<batchId>&preview=true"
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/reverse?batchId=<batchId>"
```
`apply` must return `200`; both `reverse` calls (preview and real) must also return `200` — never the
`422` this issue reported. If this ever regresses, `SqliteImportActionService.ApplyBatchAsync`'s own
`MarkImportBatchAppliedAsync` call (gated on `TryApplyBatchAsync` returning `null`) is the one place
that sets `Status`/`AppliedAt` for every caller — check it wasn't bypassed by a new caller of
`ApplyBatchAsync` or `TryApplyBatchAsync` added elsewhere.

---

## 4. `batchId`-mode alias (#154)

`POST /import` can apply an already-staged batch directly, without re-uploading a file:
```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"skip"}}' \
  "http://localhost:8080/api/v1/import/preview"
```
Copy the `batchId` from the response, then:
```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import?batchId=<batchId>"
```
Must return `200` (the `skip` policy leaves nothing pending) and apply the previewed batch — proves `batchId` mode is a genuine alias for `POST /import/actions/apply`, not a dead code path.

---

## 5. Discard (#154)
```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' \
  "http://localhost:8080/api/v1/import/preview"
```
Copy the `batchId`, then:
```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/discard?batchId=<batchId>"
curl -s "http://localhost:8080/api/v1/import/actions?batchId=<batchId>"
```
Discard must return `204`; every action in that batch must now show `"status":"Discarded"` — nothing was ever applied, since creation is deferred to apply time.

---

## 6. Reverse (undo) (#59)
```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' \
  -w "\n%{http_code}\n" \
  "http://localhost:8080/api/v1/import"
```
Note the returned `batchId` — this must be `200` (a clean apply, nothing pending) so there is a
genuinely `Applied` batch to reverse.
```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/reverse?batchId=<batchId>&preview=true"
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/reverse?batchId=<batchId>"
curl -s "http://localhost:8080/api/v1/import/actions?batchId=<batchId>"
```
`preview=true` must return `200` without changing anything; the real call must also return `200`.
The actions listing must still show every action `"status":"Applied"` afterwards — reversal never
introduces a new action status; the batch's own record being gone is the only signal it was undone
(confirm via `GET /api/v1/admin/audit` or `Quotinator.Tools.DbInspector` against `ImportBatches`
showing `IsDeleted=1` for this batch, since there is no `GET /import-batches` listing endpoint).
```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/reverse?batchId=<batchId>"
```
Reversing the same batch again must now return `404` (already reversed, treated as absent).
```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' \
  -w "\n%{http_code}\n" \
  "http://localhost:8080/api/v1/import"
```
Re-importing the same file after reversal must succeed (`200`/`202`, never a silent no-op) and the
curated quotes must be reachable again via `GET /api/v1/quotes/search?q=Airplane&field=source` —
this is the resurrection fix (#59) proven live, not just by `ApplyResolvedActionAsync_ReAddAfterSoftDelete_ResurrectsSoftDeletedRow`.
Finally, with at least one other batch applied after the one just reversed (true for a normal
database with seed + this import history), attempt to reverse an older batch out of order:
```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/reverse?batchId=<an older batchId>"
```
Must return `422` — the strict LIFO stack rule (#59): only the most recently applied batch still
live may be reversed, regardless of whether it shares any entities with the older one.

---

## 7. Bodyless request validation (#154)

A `POST /import` with no body, no `Content-Type`, and no `batchId` must be rejected with a clear, actionable message rather than a bare framework `400`:
```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import"
```
Must return `422` with a `detail` field ("you must provide either a file... or a batchId", paraphrased per locale) — **not** a bare `400` with no `detail` at all. This distinction matters: `WebApplicationFactory`'s in-memory TestServer handles a bodyless request differently than real Kestrel does, so the unit test suite alone cannot prove this — only this live check can. If this ever regresses to a bare `400`, `POST /import`'s handler is binding `IFormFile`/`[FromForm]` parameters automatically again instead of reading `HttpRequest` manually (see `ImportEndpoints.cs`'s `HandleImportFromRequestAsync`).
```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import?batchId=00000000-0000-0000-0000-000000000000"
```
Must return `404` (unknown batch) even with zero body/`Content-Type` — proves `batchId` mode never attempts to read the request body at all.

---

## 8. StageDirection/SoundCue Modify/decidability (#171/#172)

Both entities were Add-only before
these issues; this proves a `Complete` row blocks a silent overwrite, and a correctable row can be
Modified/decided/reversed end to end. Create a small fixture (one quote is required — `POST /import`
rejects a file with none):
```bash
cat > .claude/temp/smoke-171-172.json <<'EOF'
{
  "quotes": [{"id":"f0000001-0000-4000-8000-000000000001","quote":"Smoke test filler quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "stageDirections": [{"id":"f0000002-0000-4000-8000-000000000002","text":"A shot rings out.","imageUrl":null,"translations":{}}],
  "soundCues": [{"id":"f0000003-0000-4000-8000-000000000003","text":"Distant thunder.","soundFileUrl":null,"imageUrl":null,"translations":{}}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-171-172.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' "http://localhost:8080/api/v1/import"
```
Must return `200` with both rows added (check via `Quotinator.Tools.DbInspector` — `SELECT Id, Text, CompletenessStatus FROM StageDirections WHERE Id = 'f0000002-...'`). Re-import the same ids with a
changed `text` under `{"duplicateResolution":{"default":"review"}}` — must stage a `Pending` `Modify`
action for each (`GET /import/actions?status=pending`) with `ambiguousFields: ["text"]`. Decide each
with `{"stageDirectionText":{"choice":"replace"},"markCompletenessAs":"Complete"}` /
`{"soundCueText":{"choice":"replace"},"markCompletenessAs":"Complete"}`, then
`POST /import/actions/apply?batchId=...` — confirm the corrected text and `CompletenessStatus: Complete`
via DbInspector. Re-import the same ids again with another changed `text` under `review` policy — must
now stage `Blocked`, not `Pending` (`GET /import/actions?status=Blocked`), and the on-disk text must be
unchanged — proves a `Complete` row can no longer be silently overwritten. Finally, exercise
correct/apply/reverse on a still-correctable row: single-shot re-import a changed `text` under
`newest-wins` (nothing pending, applies immediately, `ImportBatches.Status` set to `Applied` by this
direct-apply path — the two-phase decide→apply path used above does **not** set it, a known
pre-existing gap, see #171/#172's plan docs), confirm the write via DbInspector, then
`POST /import/actions/reverse?batchId=...` (`preview=true` first, then for real) and confirm the
pre-correction text is restored via DbInspector.

---

## 9. Person: explicit id, Modify/decidability, dateOfBirth/dateOfDeath (#173)

Person was Add-only
before this issue and never had a write path for `dateOfBirth`/`dateOfDeath`; this proves both a
`Complete` Person blocks a silent overwrite and a correctable Person can be Modified/decided end to
end, plus exercises the lowercase-explicit-id reversal fix found live during this issue's own T2
pass. Create a small fixture (one quote is required):
```bash
cat > .claude/temp/smoke-173.json <<'EOF'
{
  "quotes": [{"id":"f0000004-0000-4000-8000-000000000004","quote":"A #173 smoke test filler quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":"Smoke Test Person","type":"movie","genres":[],"translations":{}}],
  "people": [{"id":"f0000005-0000-4000-8000-000000000005","name":"Smoke Test Person","dateOfBirth":"1950-01-01","dateOfDeath":null}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-173.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' "http://localhost:8080/api/v1/import"
```
Must return `200` with the Person added (check via `Quotinator.Tools.DbInspector` — `SELECT Id,
Name, DateOfBirth, DateOfDeath, CompletenessStatus FROM People WHERE Id = 'f0000005-...'` — note
the id is deliberately lowercase, as a file-authored explicit id always is). Re-import the same id
with a changed `dateOfBirth` under `{"duplicateResolution":{"default":"review"}}` — must stage a
`Pending` `Modify` action (`GET /import/actions?status=pending`) with `ambiguousFields:
["dateOfBirth"]`. Decide with `{"personDateOfBirth":{"choice":"replace"},"markCompletenessAs":
"Complete"}`, then `POST /import/actions/apply?batchId=...` — confirm the corrected `DateOfBirth`
and `CompletenessStatus: Complete` via DbInspector. Re-import the same id again with another changed
`dateOfBirth` under `review` policy — must now stage `Blocked`, not `Pending`
(`GET /import/actions?status=Blocked`), and the on-disk value must be unchanged — proves a
`Complete` Person can no longer be silently overwritten.

**Correction (2026-07-31):** the paragraph below used to describe reversing the *same* Person from
the steps above under `newest-wins`, expecting it to "apply immediately, nothing pending." That
cannot happen: `CompletenessGuard.ShouldBlock` (`ImportActionPlanner.cs`, #168) is evaluated against
the value a policy would actually *write*, not the raw incoming value — so once a row is `Complete`,
every policy except `skip` blocks a genuine field change, `newest-wins` included. Re-running that
exact sequence against a `Complete` Person stages `Blocked` again, exactly like the paragraph above,
never a clean apply. The lowercase-id reversal fix is real and still needs proving, but only against
a fresh row that was never marked `Complete` — reversing a `Modify`-only batch never touches
`IsDeleted` at all; only reversing the row's own `Add` does. Use a **second, brand-new** Person for
this part:

```bash
cat > .claude/temp/smoke-173-addonly.json <<'EOF'
{
  "quotes": [{"id":"f0000005-0000-4000-8000-000000000008","quote":"A #173 add-only smoke test quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":"Smoke Test Person AddOnly","type":"movie","genres":[],"translations":{}}],
  "people": [{"id":"F0000007-0000-4000-8000-000000000007","name":"Smoke Test Person AddOnly","dateOfBirth":"1985-05-05","dateOfDeath":null}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-173-addonly.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
```
Note the file's own id is deliberately uppercase (`F0000007-...`) — this is the case-sensitivity
regression's actual reproduction shape: a Guid-typed repository call used to silently force-uppercase
before comparing, matching zero rows against the lowercase-canonicalized stored id, so the row would
otherwise stay visibly present with `IsDeleted = 0` despite the endpoint reporting success. Copy the
returned `batchId`, then:
```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/reverse?batchId=<batchId>&preview=true"
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/reverse?batchId=<batchId>"
```
Both must return `200`; confirm via DbInspector (`SELECT Id, IsDeleted FROM People WHERE Id =
'f0000007-0000-4000-8000-000000000007'`) that `IsDeleted` genuinely flips to `1`. Re-import the exact
same fixture one more time — must stage as a fresh `Add` (not `Modify`, which would mean the reversal
silently no-op'd and the row was never truly gone), and `IsDeleted` must be back to `0` afterward.

---

## 10. Series/Universe schema, Character↔Source many-to-many identity (#179)

Character no longer
has a `SourceId` column; a Character's Source links live in `CharacterSources` instead, and today's
matching remains per-Source in meaning (only the mechanism changed — reusing a Character across
Sources is #174's job, not this one's). This proves both halves live: a brand-new Character on an
existing Source creates exactly one new `CharacterSources` link, and the same Character *name*
under a *different* Source still creates a separate row (no premature cross-Source reuse).
```bash
cat > .claude/temp/smoke-179.json <<'EOF'
{"quotes": [{"id":"a0000001-0000-4000-8000-000000000001","quote":"A #179 smoke test line.","originalLanguage":"en","source":"Airplane!","date":"1980","character":"Striker (Smoke Test)","author":null,"type":"movie","genres":[],"translations":{}}]}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-179.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
```
Must return `200`. Confirm via `Quotinator.Tools.DbInspector` — `SELECT COUNT(*) FROM
CharacterSources;` must have increased by exactly 1, and `SELECT c.Name, s.Title FROM Characters c
JOIN CharacterSources cs ON cs.CharacterId = c.Id JOIN Sources s ON s.Id = cs.SourceId WHERE
c.Name = 'Striker (Smoke Test)';` must show one row linking to `Airplane!`. Then re-import the same
character name under a different Source:
```bash
cat > .claude/temp/smoke-179b.json <<'EOF'
{"quotes": [{"id":"a0000002-0000-4000-8000-000000000002","quote":"A second #179 smoke test line, same character, different source.","originalLanguage":"en","source":"Monty Python and the Holy Grail","date":"1975","character":"Striker (Smoke Test)","author":null,"type":"movie","genres":[],"translations":{}}]}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-179b.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
```
Must return `200`. `SELECT COUNT(*) FROM Characters WHERE Name = 'Striker (Smoke Test)';` must now
be `2` — a *second*, separate Character row, each linked to its own Source via `CharacterSources`
— proving today's per-Source matching genuinely survived the mechanism change unchanged, not
silently reused across Sources.

---

## 11. Sources.Date populated from the resolving quote (#191)

A Source discovered implicitly from a
quote (no `sources[]` entry naming it) previously never carried a date, even when the resolving
quote had one. Re-imports the curated file's own `Airplane!`/`1980` quote to confirm the fix reaches
a real import, not only startup seeding (already proven by a fresh container's seed — see the
aggregate query below).

**Known open gap (found 2026-07-31, not yet filed as its own issue — see #191's own T2 row 6, which
already showed `439/479`, not `479/479`, right after the fix shipped): this fix only applies to a
Source discovered *purely* by inference from a quote.** `PlanSourcesAsync` (the explicit `sources[]`-
entry path #162/#180 use) was deliberately left untouched by #191. An entry that names a Source but
omits `date` (as `quotinator-series-universe.json`'s Series-linking entries do — e.g. `"Frozen"`,
`"Jurassic Park"`) creates the row with `Date = NULL` up front, and no later quote ever backfills it,
even when that quote carries a real date and is processed in a later-seeded file. Confirmed live via
`Frozen` (`NikhilNamal17_popular-movie-quotes.json` carries `"date": "2013"` for it, but
`quotinator-series-universe.json` seeds its date-less `sources[]` entry first per `manifest.json`'s
file order) and reproduces identically on the `Jurassic Park` cross-check below — do not be surprised
if `Date` is `NULL` there; it is this gap, not a new regression.
```bash
curl -s "http://localhost:8080/api/v1/quotes/search?q=Airplane&field=source"
```
Check the `date` field on the returned item(s) is `"1980"`, not `null` — this Source already exists
from seeding, so this call only confirms the read path surfaces the seeded date correctly; it does
not by itself re-exercise `ResolveSourceAsync`. To confirm the fix on a *fresh* seed specifically
(the actual code path this issue fixed), inspect the database directly, matching the issue's own
reproduction steps:
```bash
docker stop -t 15 <container>
docker cp <container>:/app/data/quotinatordata.db .claude/temp/inspect-191.db
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db ".claude/temp/inspect-191.db" \
  --sql "SELECT COUNT(*) AS sources, SUM(CASE WHEN Date IS NOT NULL THEN 1 ELSE 0 END) AS have_date FROM Sources WHERE IsDeleted = 0"
```
`have_date` must be nonzero and a large majority of `sources` (roughly 400+ of 479 on the current
bundled dataset) — before the fix this was always `0`. Cross-check one title with no `sources[]`
entry at all (implicit-discovery path, the case #191 actually fixes):
```bash
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db ".claude/temp/inspect-191.db" \
  --sql "SELECT Title, Type, Date FROM Sources WHERE Title = 'Airplane!' AND IsDeleted = 0"
```
Must return `Date = 1980`. Then cross-check a title known to have a date-less explicit `sources[]`
entry (the gap noted above):
```bash
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db ".claude/temp/inspect-191.db" \
  --sql "SELECT Title, Type, Date FROM Sources WHERE Title = 'Jurassic Park' AND IsDeleted = 0"
```
Currently returns `Date = NULL` — expected until the open gap above is fixed, not a fresh failure.

---

## 12. Canonicalize explicit ids at capture (Source/Person/StageDirection/SoundCue/Conversation) (#209)

A file-authored explicit id previously reached storage in whatever raw casing the file used,
never canonicalized; a `Guid`-typed lookup (which force-uppercases) then silently failed to find a
non-canonically-stored row, even though the same row resolved fine via a join. Also proves the
ConversationLines FOREIGN KEY fix: the bundled curated file's own Conversations reference
StageDirections/SoundCues by id, and #209's own fix would have broken that reference if left
incomplete — a clean seed with no `SQLite Error 19` is itself part of this check, not just the
import below.
```bash
cat > .claude/temp/smoke-209.json <<'EOF'
{
  "quotes": [{"id":"f6000001-0000-4000-8000-000000000001","quote":"A #209 smoke test line.","originalLanguage":"en","source":"209 Smoke Test Film","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "sources": [{"id":"f6000002-0000-4000-8000-000000000002","title":"209 Smoke Test Film","type":"movie"}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-209.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/sources/f6000002-0000-4000-8000-000000000002"
curl -s "http://localhost:8080/api/v1/quotes/f6000001-0000-4000-8000-000000000001"
```
The import must return `200`. The masterdata lookup — using the file's own lowercase id in the URL,
the exact scenario that originally 404'd — must also return `200`, with `id` shown canonicalized (as
lowercase, ADR 012's system-wide convention) in the response. The quote lookup must resolve `source`
to `"209 Smoke Test Film"` via the Quote→Source join, proving the fix didn't break the join to make
the masterdata lookup work.

---

## 13. Pagination contract: pageSize=0, max 500, default 20, page-beyond-last (#195)

`/quotes`,
`/admin/audit`, and `/import/actions` share one pagination contract; this proves it holds live on
all three, not just at the unit-test/stub level. The two audit/import readers were caught passing
`pageSize=0` straight into `LIMIT @pageSize` instead of translating it to `LIMIT -1` during this
issue's own T2 pass — no existing unit test could catch it, since the stub readers those tests use
echo their input back rather than exercising real SQL. Run this section after the sections above so
`/admin/audit` and `/import/actions` already have rows to page through.
```bash
curl -s "http://localhost:8080/api/v1/quotes?pageSize=0"
curl -s "http://localhost:8080/api/v1/admin/audit?pageSize=0" -H "X-Api-Key: <your admin key>"
curl -s "http://localhost:8080/api/v1/import/actions?pageSize=0"
```
On all three: `items` must contain every row (not zero), and `pageSize` in the response must equal
`totalCount` — the effective-size contract, not the literal `0` requested.
```bash
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/quotes?pageSize=501"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/admin/audit?pageSize=501" -H "X-Api-Key: <your admin key>"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import/actions?pageSize=501"
```
All three must return `422` — `pageSize` above 500 is rejected, never silently clamped.
```bash
curl -s "http://localhost:8080/api/v1/admin/audit" -H "X-Api-Key: <your admin key>"
curl -s "http://localhost:8080/api/v1/import/actions"
```
`pageSize` in both responses must be `20`, not the endpoints' old default of `50` — since both
tables already have rows from the sections above, this also confirms the default is genuinely
applied, not just an artifact of an empty table.
```bash
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/quotes?pageSize=500&page=99"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/admin/audit?pageSize=1&page=999999" -H "X-Api-Key: <your admin key>"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import/actions?pageSize=1&page=999999"
```
All three must return `422` (page beyond the last page) — true for `/admin/audit` and
`/import/actions` only because the sections above already populated at least one row in each; on a
database with zero rows in a table, page 1 of nothing is not "beyond the last page" and this would
return `200` instead (see `PaginationParsingTests.ValidatePageBeyondLast_ZeroTotalPages_ReturnsNull`).
The remaining case — `page`/`pageSize` publishing as `integer`, not `string`, on the live spec for
all three paths — is **not** part of this manual checklist. It was originally a `curl | grep`
check here, but grepping a pretty-printed, multi-line JSON body for a nested field is fragile (the
first version of that exact command was wrong — it assumed single-line JSON and never matched
anything) and its pass/fail requires a human or AI to eyeball the output. It is now
`OpenApiSpecEndpointTests.cs` — a `WebApplicationFactory`-based test that fetches the real
`/openapi/v1.json` through the full pipeline and asserts the type via `JsonDocument`, so it runs
deterministically in every `dotnet test` instead of requiring a live container. See "Standard
pagination contract" earlier in this file for why this needs a dedicated live-pipeline test at all,
given `NumericParameterSchemaTransformer` already has its own unit tests.

---

## 14. Quotes.Id case-insensitive lookup (#210)

Quotes.Id canonicalizes to lowercase, the same
convention every other entity uses (`EntityIdentity.StableId`, `GuidExtensions.ToCanonicalId`) —
this project's single settled id format after two prior revisions (ADR 012's revision history).
Before #210's first pass, `GET /quotes/{id}` had no case-insensitive read-side mitigation at all —
the one fully-unmitigated gap of this kind found across the whole codebase.
```bash
cat > .claude/temp/smoke-210.json <<'EOF'
{"quotes": [{"id":"F0000210-0000-4000-8000-000000000210","quote":"A #210 smoke test quote with an uppercase explicit id.","originalLanguage":"en","source":"Smoke Test Film 210","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-210.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/quotes/f0000210-0000-4000-8000-000000000210"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/quotes/F0000210-0000-4000-8000-000000000210"
```
Import must return `200`. Both `GET` calls (lowercase URL casing, and the file's own original
uppercase casing) must return `200` with the same quote, and the response's own `id` field must be
the canonical **lowercase** form (`f0000210-...`) regardless of the uppercase casing the file
supplied — proving both the capture-time canonicalization and the case-insensitive read together.

---

## 15. ConversationLines.QuoteId FK safety (#210's casing-unification revision)

A conversation line
referencing a quote by an id whose casing doesn't match the quote's own now-canonical form must not
violate `ConversationLines`' real `FOREIGN KEY` constraint to `Quotes(Id)` — the same bug class #209
found for `StageDirectionId`/`SoundCueId`, now also covering `QuoteId`.
```bash
cat > .claude/temp/smoke-210-conv.json <<'EOF'
{
  "quotes": [{"id":"f0000210-0000-4000-8000-000000000211","quote":"A #210 conversation-line smoke test quote.","originalLanguage":"en","source":"Smoke Test Film 210b","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "conversations": [{"id":"f0000210-0000-4000-8000-000000000212","description":"A #210 smoke test conversation.","lines":[{"order":1,"type":"quote","quoteId":"F0000210-0000-4000-8000-000000000211"}]}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-210-conv.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
```
The quote's own `id` is lowercase; the conversation line's `quoteId` deliberately uses the uppercase
form of the same id. Must return `200`, not a `SQLite Error 19: FOREIGN KEY constraint failed`.

---

## 16. Systemic id-case guard (#210's scope expansion)

`Quotinator.Data.Diagnostics.SqlIdCaseGuard`
scans every SQL query in the codebase for an unwrapped id-comparison at build/test time
(`SqlConstant_PassesIdCaseGuard`/`AssembledQuery_PassesIdCaseGuard` in both `Quotinator.Core.Tests`
and `Quotinator.Data.Tests`, `RepositorySqlFactory_PassesIdCaseGuard` in
`Quotinator.Data.Tests.Repositories.RepositorySqlGuardTests`) — this is unit-test-tier coverage, not a
live/T2 check, and needs no separate Docker verification beyond the Quotes.Id scenario above; listed
here only so a future reader knows why `RepositorySql.cs`'s generic `SelectById`/`SoftDelete`/etc. are
now `LOWER()`-wrapped (ADR 012's system-wide lowercase revision — see that ADR for why `LOWER()`, not
`UPPER()`) even though no single T2 scenario exercises them directly.

---

## 17. Read-time presentation normalization for string-typed id-reference fields (#210's third revision)

`batchId`/`entityId`/`existingBatchId`/`recordId` are `string`-typed (not `Guid`-typed),
so unlike `id` fields they get no automatic lowercase rendering from `System.Text.Json`'s `Guid`
serialization default; a `LOWER(...) AS ColumnName` wrap was added to `Sql.SystemImportActions
.SelectColumns` and `Sql.SystemAudit.SelectPaged` so these fields render canonically regardless of
what casing is actually stored. Confirms a real end-to-end HTTP round trip, not just the unit-test-tier
`ExistingBatchId_RoundTripsCorrectly`:
```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' \
  "http://localhost:8080/api/v1/import"
curl -s "http://localhost:8080/api/v1/import/actions?status=pending&pageSize=1"
curl -s "http://localhost:8080/api/v1/admin/audit?pageSize=1" -H "X-Api-Key: <your admin key>"
```
The import response's own `batchId` and every `quoteId` under `pendingActionIds`, and the
`/import/actions` response's `batchId`/`entityId`/`existingBatchId`, and the `/admin/audit` response's
`recordId`, must all be lowercase — freshly generated `Guid`s render lowercase from
`GuidExtensions.ToCanonicalId()` regardless, so this mainly confirms no regression; the actual
read-time fix (rendering an *already-uppercase* stored value as lowercase) is proven at the SQLite
integration-test tier by `ExistingBatchId_RoundTripsCorrectly`, which writes a deliberately mixed-case
fixture directly (bypassing capture-time canonicalization) and reads it back through this exact query
path — a live T2 run cannot easily manufacture pre-existing non-canonical data through the API alone,
since every write path now canonicalizes at capture time.

---

## 18. Uniform SELECT-list wrapping via `IEntityColumnMetadata` (#210's follow-on round)

`RepositorySql.cs`'s
generic queries (`SelectById`, `SelectByIds`, `SelectDeleted`, `SelectByForeignKey`, `SelectJunctionRow`,
`SelectPage`) build an explicit column list via a caller-supplied `IEntityColumnMetadata` instead of
`SELECT *`, wrapping every id column the same way hand-written `Sql.cs` queries do. Confirms every
generic-repository-backed masterdata endpoint still returns correct data and lowercase ids after the
`SELECT *` removal:
```bash
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/sources?pageSize=2"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/characters?pageSize=2"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/people?pageSize=2"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/series?pageSize=2"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/universes?pageSize=2"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/conversations?pageSize=2"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/stagedirections?pageSize=2"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/soundcues?pageSize=2"
```
All must return `200` with populated `items` and lowercase `id` fields — these endpoints all go
through `SqliteRepository<T>`/`SqliteRestorableRepository<T>`'s generic `GetPageAsync`/`GetByIdAsync`,
the only live paths that exercise `RepositorySql`'s rewritten queries end to end (Characters'
`CharacterSources` many-to-many link also exercises `SqliteLinkRepository`). Also confirm
`GetByIdAsync`'s case-insensitive lookup survived the rewrite — fetch one of the returned ids from
`GET .../sources/{id}` with both its original casing and an uppercased version; both must return `200`
with the same, lowercase-rendered `id`.

---

## 19. `batchId` validated explicitly on `/actions/apply`, `/actions/discard`, `/actions/reverse`; request logging reports the real final status code

Found live via manual Visual Studio testing (T1), not
this checklist: all three endpoints declared `batchId` as a required, non-nullable minimal-API
parameter, so an omitted `batchId` threw `BadHttpRequestException` at the binding layer before the
handler ever ran. The global safety net (`BadRequestExceptionHandler`) caught it and returned `422` —
but with a message hard-coded to numeric parameters ("Numeric parameters (yearFrom, yearTo, ...) must
be whole numbers"), actively wrong for a missing `batchId`. Separately, the completion log line for
that same request read `→ 200`, not the `422` the client actually received — `Program.cs` registered
`UseExceptionHandler()` before `RequestLoggingMiddleware`, so the exception unwound through the
logging middleware's `finally` block (which reads `context.Response.StatusCode`, still the untouched
default at that point) before the exception handler further out ever set the real status. Fixed by
declaring `batchId` as `string?` and validating it explicitly at the point of origin (mirroring the
"Numeric query parameter binding pattern" convention), and by moving `RequestLoggingMiddleware`'s
registration to before `UseExceptionHandler()` so it wraps the exception handler instead of being
wrapped by it.
```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/apply"
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/discard"
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/reverse"
```
All three must return `422` with `"detail":"You must provide a batchId."` — never the generic
"Numeric parameters..." message. With `Quotinator__LogRequests=true`, `docker logs` for each of these
requests must show `→ 422`, not `→ 200`. Re-run a normal `apply` with a real `batchId` (see the
"Import and staged-action review workflow" section above) to confirm the fix didn't break the happy
path — still `200`.

---

## 20. Character Modify/decidability via the widened `characters[]` schema, case-insensitive Source natural-key matching (#175)

Before this issue, `characters[]` only ever
supported Correction (`id` present, matched by id) or brand-new-via-natural-key; there was no way to
correct an existing Character's `Name` through the staging/decide pipeline the way Source/Person/
StageDirection/SoundCue already could. The widened schema adds `sourceTitle`/`sourceType` (required
unconditionally, mirroring `source`'s own shape) so a no-id entry can resolve through ADR 013's real
Type-anchored, Series-scoped matching algorithm rather than a bare Name lookup. Also proves a real
T2-only bug this issue's own re-verification pass found and fixed: an explicit `characters[]` id that
matches nothing was being silently discarded in favour of a freshly-computed `EntityIdentity`-derived
id — unlike `PlanSourcesAsync`'s own established `canonicalId ?? EntityIdentity.SourceId(...)`
precedent, which a unit-test-only pass would not have caught (the unit suite's own two tests for this
were written and initially passed against the *bug*, since they never independently verified which id
actually landed in the database — the T2 walkthrough below is what surfaces it).
```bash
cat > .claude/temp/smoke-175-add.json <<'EOF'
{
  "quotes": [{"id":"a1111175-0000-4000-8000-000000000001","quote":"A #175 smoke test creation quote.","originalLanguage":"en","source":"Airplane!","date":"1980","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "characters": [{"name":"Smoke Test New Character","sourceTitle":"Airplane!","sourceType":"movie"}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-175-add.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
```
Must return `200`. `GET /api/v1/masterdata/characters` must now include "Smoke Test New Character"
linked to the existing `Airplane!` Source (no id supplied — resolved via ADR 013's algorithm finding
no candidate, then a genuine Add). Next, correct an existing Character by id under `review`:
```bash
cat > .claude/temp/smoke-175-modify.json <<'EOF'
{
  "quotes": [{"id":"a1111175-0000-4000-8000-000000000002","quote":"A #175 smoke test modify-trigger quote.","originalLanguage":"en","source":"Airplane!","date":"1980","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "characters": [{"id":"<an existing Character id from the query above>","name":"Renamed Via Smoke Test","sourceTitle":"Airplane!","sourceType":"movie"}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-175-modify.json" -F 'settings={"duplicateResolution":{"default":"review"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
```
Must return `202` with one pending id, and `GET /api/v1/import/actions?status=pending` must show
`"ambiguousFields":["name"]` only — not `sourceId`, confirming the Modify payload's unchanged
`SourceId` doesn't spuriously trip `FieldMergeResolver`. Decide and apply:
```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" -H "Content-Type: application/json" -d '{"characterName":{"choice":"replace"},"markCompletenessAs":"Complete"}' "http://localhost:8080/api/v1/import/actions/<id>/decide"
curl -s -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/apply?batchId=<batchId>"
curl -s "http://localhost:8080/api/v1/masterdata/characters/<id>"
```
`name` must read "Renamed Via Smoke Test" and `completenessStatus` must be `Complete`. Re-attempt
another Modify against the same id under `review` — must now stage `Blocked`, not `Pending`
(`GET /api/v1/import/actions?status=Blocked`), and the on-disk name must be unchanged, proving a
`Complete` Character can no longer be silently overwritten (the same guarantee Source/Person/
StageDirection/SoundCue already have). Next, the explicit-id-honoured-on-Add fix itself:
```bash
cat > .claude/temp/smoke-175-explicit-add.json <<'EOF'
{
  "quotes": [{"id":"a1111175-0000-4000-8000-000000000005","quote":"A #175 smoke test explicit-id-add quote.","originalLanguage":"en","source":"Airplane!","date":"1980","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "characters": [{"id":"F5111175-0000-4000-8000-000000000175","name":"Explicit Id Character","sourceTitle":"Airplane!","sourceType":"movie"}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-175-explicit-add.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/masterdata/characters/f5111175-0000-4000-8000-000000000175"
```
The import's own file uses the id in uppercase; the masterdata lookup uses the canonical lowercase
form. Both must succeed, and the returned `id` must be the lowercase-canonicalized form of the file's
own id — never an unrelated `EntityIdentity`-derived one. Finally, the case-insensitive Source
natural-key fix (`Sql.Sources.SelectIdByTitleAndType`/`SelectExistingByTitleAndType`):
```bash
cat > .claude/temp/smoke-175-source-casing.json <<'EOF'
{
  "quotes": [{"id":"a1111175-0000-4000-8000-000000000006","quote":"A #175 smoke test source-casing quote.","originalLanguage":"en","source":"AIRPLANE!","date":"1980","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "characters": [{"name":"Case Insensitive Source Character","sourceTitle":"AIRPLANE!","sourceType":"movie"}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-175-source-casing.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
curl -s "http://localhost:8080/api/v1/masterdata/sources?pageSize=500"
```
Despite `AIRPLANE!` appearing in both the quote's own `source` and the character's `sourceTitle`,
there must still be exactly one `"title":"Airplane!"` row in the Sources list — proving the entry
resolved to the pre-existing Source rather than creating a case-sensitive duplicate. Clean up:
```bash
rm -f .claude/temp/smoke-175-*.json
```

---

## 21. Bulk-decide a staged batch via file export/import — CSV and JSON (#163)

`GET
/import/actions/export` flattens every decidable field of a batch's Pending/Decided/Blocked Modify
actions into rows; `POST /import/actions/bulk-decide` reads an edited version of that export back
and applies each row's decision. Proves the export→edit→bulk-decide→apply round trip works over the
real wire format in both directions, plus two live-only bugs found and fixed during this issue's own
T2 pass that no unit test could catch (see below).
```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' \
  "http://localhost:8080/api/v1/import"
```
Note the returned `batchId` (must be `202`, with pending Quote Modify actions to export).
```bash
curl -s "http://localhost:8080/api/v1/import/actions/export?batchId=<batchId>&format=json" -o /tmp/export.json
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  -F "batchId=<batchId>" -F "file=@/tmp/export.json" \
  "http://localhost:8080/api/v1/import/actions/bulk-decide?batchId=<batchId>"
```
Must return `200` with `errors: []` and `actionsDecided` matching the batch's own pending-action
count — submitting export's own unmodified JSON output back into bulk-decide must always round-trip
cleanly with zero errors. **This is the exact scenario that caught the first live-only bug**: ASP.NET's
app-wide camelCase JSON default (`ConfigureHttpJsonOptions` in `Program.cs`) means export's own output
is genuinely camelCase, but `ParseJsonRows`'s `element.Deserialize<ImportActionFieldRow>()` call had no
explicit `JsonSerializerOptions`, silently falling back to `System.Text.Json`'s case-sensitive,
PascalCase-only library default — every row failed with "missing required properties" despite the data
being present. Every unit-test-level round trip used bare `JsonSerializer` calls on both sides, which
silently agreed on PascalCase and never exercised the app's real camelCase configuration — only a live
HTTP round trip through the actual pipeline surfaces this class of bug. Fixed via a dedicated
`JsonSerializerOptions { PropertyNameCaseInsensitive = true }` passed explicitly to the `Deserialize`
call. Apply the batch and repeat via CSV to confirm the second wire format too:
```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/apply?batchId=<batchId>"
```
```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' \
  "http://localhost:8080/api/v1/import"
curl -s "http://localhost:8080/api/v1/import/actions/export?batchId=<new batchId>&format=csv" -o /tmp/export.csv
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  -F "batchId=<new batchId>" -F "file=@/tmp/export.csv" -F "format=csv" \
  "http://localhost:8080/api/v1/import/actions/bulk-decide?batchId=<new batchId>&format=csv"
```
Must also return `200` with `errors: []`. Malformed-row resilience — edit one row of the CSV to an
invalid `Decision` value, leave the rest untouched, resubmit:
```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  -F "batchId=<new batchId>" -F "file=@/tmp/export-with-one-bad-row.csv" -F "format=csv" \
  "http://localhost:8080/api/v1/import/actions/bulk-decide?batchId=<new batchId>&format=csv"
```
Must return `200` (never `422` for the whole request) with exactly one entry in `errors[]` naming the
bad row's `actionId`, and every other row's action still decided — "one bad row never aborts the
rest of the file", matching `POST /import`'s own established contract. Unknown-format and missing-key
checks:
```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" -F "batchId=<batchId>" -F "file=@/tmp/export.json" "http://localhost:8080/api/v1/import/actions/bulk-decide?batchId=<batchId>&format=xml"
curl -s -w "\n%{http_code}\n" -X POST -F "batchId=<batchId>" -F "file=@/tmp/export.json" "http://localhost:8080/api/v1/import/actions/bulk-decide?batchId=<batchId>"
```
Must return `422` (unknown `format`) and `401` (no `X-Api-Key`) respectively. **The second live-only
bug**: a request with neither `batchId` nor a multipart body at all —
```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/bulk-decide"
```
Must return `422` with `"detail":"You must provide a batchId."` — before the fix this returned a bare,
uninformative `400` with no `detail` at all, because the endpoint originally bound `IFormFile? file`
directly as a minimal-API parameter, which requires a form content-type to even attempt binding; a
request with no `Content-Type`/body fails that check at the framework's own routing/binding layer, not
as a normal thrown exception, bypassing `BadRequestExceptionHandler` entirely — the exact same bug
class `POST /import` fixed earlier (see "Bodyless request validation" (#154) above) but never retrofitted
onto this newer endpoint. Fixed by switching the parameter to `HttpRequest request` and checking
`batchId`, then `request.HasFormContentType`, manually before ever attempting to read the form — mirroring
`HandleImportFromRequestAsync`'s existing pattern exactly.

---

## 22. Per-source conflict-resolution rule files and title-alias files (#181) — fresh 4-file seed produces zero pending actions

Every bundled file (`quotinator-curated.json`,
`quotinator-series-universe.json`, `NikhilNamal17_popular-movie-quotes.json`,
`vilaboim_movie-quotes.json`) runs under `review` policy with its own `ruleFile`/`sourceAliasFile`.
A `ConflictResolutionRule` auto-resolves a genuinely ambiguous field on an already-seen entity id
(Modify path only); a `SourceAliasRule` corrects a misspelled/inconsistent raw `(title, type)` to
the already-canonical Source *before* Source resolution ever runs, so it applies to both a
first-seen Add and a re-seen Modify, and prevents a duplicate Source row from being created for
the wrong spelling in the first place (a `ConflictResolutionRule` alone cannot do this — it only
ever corrects what a Quote's own field *displays*, never which Source row it links to). Confirm a
fresh container with a stock image:
```bash
docker run --rm -p 8080:8080 -e Quotinator__AdminApiKey=<your admin key> quotinator:local
curl -s http://localhost:8080/api/v1/version
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import/actions?status=pending"
```
`/version` must show `quotes: 799` (the current bundled-data total). `/import/actions?status=pending`
must return `200` with an **empty** `items` array — no file should be left "staged awaiting review".
If any are, `docker logs` will show `"<file>" left staged awaiting review — batch "<id>", N action(s)
pending a decision`; inspect via `GET /import/actions?batchId=<id>` to see which entity/field lacks a
rule or alias. Cross-check for duplicate Sources directly, using `Quotinator.Tools.DbInspector`
against a copy of the running container's database (`docker cp <container>:/app/data/quotinatordata.db .claude/temp/inspect-181.db`):
```bash
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db ".claude/temp/inspect-181.db" \
  --sql "SELECT Title, Type, COUNT(*) AS c FROM Sources WHERE IsDeleted = 0 GROUP BY LOWER(Title), Type HAVING c > 1"
```
Must return **no rows** — any row here is a genuine duplicate Source that slipped through both the
rule and alias mechanisms.

---

## 23. Rule file live-read proof (#181)

Proves the lookup genuinely reads the rule file's live content, not a cached or hardcoded value —
live-verified 2026-07-25. Temporarily delete the Auntie Mame rule entirely from
`nikhilnamal17-conflict-rules.json` (`entityId: 088603c0-...`), then rebuild and run a fresh
container:
```bash
docker build -f docker/Dockerfile -t quotinator:local .
docker run --rm -p 8080:8080 -e Quotinator__AdminApiKey=<your admin key> quotinator:local
curl -s "http://localhost:8080/api/v1/import/actions?status=pending"
```
With the rule removed, that one quote's conflict must now stage `Pending` again (confirmed:
`ambiguousFields: ["date"]`) — proving the mechanism actually consults the file's content on every
seed, not a cached decision from an earlier run. Restore the rule, then change its `resolution` from
`Keep` to `Replace` and reseed again — `GET /quotes/{id}` will **not** show the change (`date` is
Source-derived, read via JOIN from `Sources.Date`, and the Source was already fixed at the film's
correct year by whichever occurrence was seen first — a per-quote rule only ever affects that Quote's
own `MergedFields` audit trail, never a Source-owned field's real stored value, the same limitation
#181's own Step 10 addendum documents). Check via `Quotinator.Tools.DbInspector` instead:
```bash
docker cp <container>:/app/data/quotinatordata.db .claude/temp/inspect-181.db
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db ".claude/temp/inspect-181.db" \
  --sql "SELECT MergedFields FROM System_ImportActions WHERE EntityId='088603c0-b35a-1b48-977d-ca08489a0cbb' AND ActionType='Modify'"
```
The row for the batch matching NikhilNamal17's own rule file must show `"date":"2005"` (the incoming
value — Replace won), confirmed changed from `"date":"1958"` under the original `Keep` rule; a
*second* row may appear for vilaboim's own separate cross-file duplicate of the same quote id,
resolved by its own unmodified rule in `vilaboim-conflict-rules.json` — unaffected by this change,
since each bundled file's rule file only governs that file's own batch. Revert both edits before
committing — this is a temporary local mutation to prove the mechanism, not a real data change.

---

## 24. ConflictResolutionRule staleness → new Stale status (#153)

A rule whose recorded
`existingRecord`/`incomingRecord` snapshot no longer matches the current staging run's real field
values is never silently reapplied; the action stages `Stale` instead of `Decided`. **Live-verified
2026-07-26 against a genuine, pre-existing data bug this mechanism caught on its first real run,
not a contrived fixture**: `nikhilnamal17-conflict-rules.json`'s Zootopia rule
(`entityId: 10e3fb48-...`, governing `quoteText` with `Keep`) had its `existingRecord`/
`incomingRecord` snapshot recorded with a straight apostrophe (`Life's`), while the real bundled
`NikhilNamal17_popular-movie-quotes.json` entry uses a curly one (`Life’s`, `’`) — a genuine
drift between the rule's recorded assumption and reality. Reproduce on the *unfixed* text to see the
mechanism catch it, matching the state before this issue's own fix landed:
```bash
docker build -f docker/Dockerfile -t quotinator:local .
docker run --rm -p 8080:8080 -e Quotinator__AdminApiKey=<your admin key> quotinator:local
curl -s -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/admin/database/reseed"
curl -s "http://localhost:8080/api/v1/import/actions?status=stale&pageSize=0"
```
**Important**: check `/api/v1/version` first and wait for the full bundled count (`quotes: 799`)
before querying — a `status=stale`/`status=pending` check against a container that hasn't finished
its initial multi-file seed yet will read a partially-seeded, misleading state. The initial
container boot only ever stages `Add` actions for a brand-new database (nothing exists yet to
conflict with); `POST /admin/database/reseed` is what re-plans every bundled file against the
now-populated database and genuinely exercises the `Modify`/rule path — the same thing a real
redeployment against an already-seeded volume does. With the apostrophe mismatch present, `status=
stale` must return the Zootopia entity; with it fixed (current `main`), it returns an empty list —
confirm via `git stash`/checkout of the pre-fix rule file only if you need to see the "before" state
again, since the shipped rule file is already corrected.

---

## 25. SourceAliasRule staleness (#153)

An alias is stale only when the Source its own
`canonicalTitle`/`canonicalType` deterministically hashes to (`EntityIdentity.SourceId`, fixed at
creation, never recomputed on a later Modify) already exists but under a *different* current title —
a genuine rename since the alias was authored. **Two false-positive bugs were found and fixed live
via this exact check, neither catchable by unit tests alone** (every existing fixture pre-seeded the
canonical Source as a real DB row, which masked both): (1) the very first version checked only "does
a Source with this exact title exist right now," which cannot distinguish a genuine rename from the
alias's own normal, legitimate job of guiding the *first-ever* creation of a Source under its correct
name — flagged 7 real bundled aliases as stale purely because their canonical Source hadn't been
created by an earlier file yet; (2) a same-batch fix (checking `sourceIndex`, this batch's own
in-memory Add cache) still needed the *id-based* rewrite above to fully clear a `SELECT *`-by-title
query being unable to distinguish those same two cases when nothing had been indexed yet either.
Confirm both are fixed against real bundled data with a fresh container and a reseed:
```bash
docker build -f docker/Dockerfile -t quotinator:local .
docker run --rm -p 8080:8080 -e Quotinator__AdminApiKey=<your admin key> quotinator:local
curl -s http://localhost:8080/api/v1/version
curl -s -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/admin/database/reseed"
curl -s "http://localhost:8080/api/v1/import/actions?status=pending&pageSize=0"
curl -s "http://localhost:8080/api/v1/import/actions?status=stale&pageSize=0"
```
Wait for `/version` to report `quotes: 799` before checking (same partial-seed caveat as above).
Both the fresh-seed and post-reseed `status=pending`/`status=stale` checks must return `totalCount:
0` — every real bundled alias's canonical Source either already exists under its exact recorded
title, or is being legitimately created for the first time; none has actually been renamed away.

---

## 26. Rule-file override endpoints (#153)

`GET`/`POST /generate`/`DELETE` under
`/api/v1/import/rules/conflict`, and the read-only `GET /api/v1/import/rules/alias`. A fresh
container has no registered override for any bundled rule file, so the effective content is always
the bundled copy at first:
```bash
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import/rules/conflict?fileName=quotinator-curated-conflict-rules.json&origin=Bundled"
```
Must return `200` with `isOverrideActive: false` and the bundled file's own rules. Re-import the
curated file under `review` (same fixture as the "Import and staged-action review workflow" section
above) to get a real batch with a decided field to generate from:
```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' \
  "http://localhost:8080/api/v1/import"
curl -s "http://localhost:8080/api/v1/import/actions?status=pending&pageSize=1"
```
Copy one pending action's `id` and the response's own `batchId`, decide it, then generate:
```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" -H "Content-Type: application/json" \
  -d '{"quoteText":{"choice":"keep"}}' \
  "http://localhost:8080/api/v1/import/actions/<id>/decide"
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/rules/conflict/generate?fileName=quotinator-curated-conflict-rules.json&origin=Bundled&batchId=<batchId>"
```
Must return `200` with `isOverrideActive: true`, `rulesAdded` at least `1`, and the response's own
`rules` array must still contain every rule the bundled file already had — the merge-preserves-
existing-rules guarantee `EffectiveRuleFileResolver` exists for. Re-run the first `GET` call from
this section again — it must now return `isOverrideActive: true` too, proving the override actually
took effect for reads. Clean up the override so it doesn't affect any later section of this
checklist:
```bash
curl -s -w "\n%{http_code}\n" -X DELETE -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/rules/conflict?fileName=quotinator-curated-conflict-rules.json&origin=Bundled"
```
Must return `204`; a repeat `DELETE` of the same file must now return `404` (nothing left to
remove). Finally, the alias-candidate suggestion endpoint — read-only, no `X-Api-Key` needed:
```bash
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import/rules/alias?fileName=quotinator-curated-source-aliases.json&origin=Bundled"
```
Must return `200` with a `candidates` array. **Live-verified 2026-07-26 against real bundled data:
this is not empty** — it found 3 genuine near-duplicates the curated alias file doesn't cover
(`"When Harry Met Sally"` vs. `"When Harry Met Sally..."`, `"Avengers - Age of Ultron"` vs.
`"Avengers: Age of Ultron"` — the normalizer strips `-`/`:` identically, correctly catching this —
and `"Airplane"` vs. `"Airplane!"`, already aliased in a *different* bundled file's own alias list,
not curated's — the file-scoped design means a duplicate covered elsewhere can still surface here,
a known, accepted trade-off, not a bug). This confirms the endpoint runs cleanly end to end against
the full live `Sources` table and genuinely finds real candidates, which is the point — do not expect
an empty result on this dataset. A confirmed, verified duplicate should be filed as a data-quality
follow-up per `docs/workflow/source-verification.md`, not fixed inline as part of this checklist.

---

## 27. Per-file, per-entity-type import/seed report (#221)

Replaces the old flat `duplicates` count
everywhere a seed/import operation reports back. Confirm all four surfaces on a fresh container:
```bash
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/admin/database/seed/preview"
```
Must return `200` with a top-level `reports` array (not `totalQuotes`/`uniqueQuotes`/
`crossFileDuplicates`, all removed) — one entry per configured source file, each with a `fileName`
and an `entityTypes` object keyed by entity type (`Quote`, `Source`, etc.), each with
`new`/`modified`/`blocked`/`discarded`/`pending`/`stale` counts.
```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/admin/database/reseed"
```
Must return `200` with `quotes`/`sources`/`characters`/`people`/`series`/`universes`/
`stageDirections`/`soundCues`/`conversations` (all nine entity-type row counts) plus `reports`
(same per-file shape as the preview above). Repeat against `POST /admin/database/reset` — same
shape.
```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' \
  -w "\n%{http_code}\n" \
  "http://localhost:8080/api/v1/import"
```
Must return `200` with a top-level `report` (singular — one file, not an array) alongside the
existing `summary`/`conflicts`/`errors` fields, shaped the same as one entry from `reports` above
(`fileName` plus `entityTypes`). Re-run the same call via `POST /api/v1/import/preview` — same
`report` shape, since the report reflects the actual staged actions regardless of whether the batch
was applied.
```bash
docker logs <container> 2>&1 | grep "\[Database - Stats\]"
```
The startup log line must show all nine counts — quotes, sources, characters, people, series,
universes, stage directions, sound cues, conversations — not just the original four.

