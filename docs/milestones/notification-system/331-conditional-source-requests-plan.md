# #331 — Source refresh: conditional requests so an unchanged source is not re-downloaded

**Status:** Planning
**GitHub issue:** #331
**Tiers required:** T1, T2
**Depends on:** #330

---

## Description

Every source refresh is an unconditional full GET: no request validators, no stored response
validators, no 304 handling. Freshness is decided solely by `IsStale`, comparing the cached file's
mtime against the refresh interval.

So once the TTL expires we re-download the entire file to arrive at byte-identical content and log
`updated {File} from {Url}` — which is untrue. Nothing was updated. An operator cannot tell a real
upstream change from a routine re-fetch, and every refresh pays full transfer cost for content we
already hold.

**This must be decided without downloading the content.** Comparing a freshly downloaded payload
against the cached copy answers the question only after paying the exact cost the check exists to
avoid. `Import_FileResource.ContentHash` works that way correctly for rule-file capture and is the
wrong mechanism here. #330's hashes serve a different purpose again: they establish what the cached
bytes *are*, which is what makes a stored validator trustworthy — they are not themselves the change
check.

**URLs are the generic feature; GitHub is an extension of it.** A manifest entry's `github` block
exists only to construct a `raw.githubusercontent.com` URL — a convenience over the generic
`downloadUrl`, not a separate transport. The mechanism here is therefore HTTP-protocol-level so it
works for any `downloadUrl`, including user manifests pointing anywhere.

---

## Steps

### 1. Persist `ETag` and `Last-Modified` in #330's shape

**Status:** ⬜ Not started

Two more fields in the sidecar DTO and the `Import_FileMetadata` row, with #330's reconciliation rule
applying unchanged. A validator whose stored content hash no longer matches the file on disk is stale
by that same rule and **must not be sent** — sending it would claim we hold content we do not.

Adding the columns re-applies #330's migration discipline: append-only migration, baseline updated in
the same commit, schema-drift test extended.

### 2. Send the conditional request when validators exist

**Status:** ⬜ Not started

`If-None-Match` when an ETag is stored; `If-Modified-Since` when only `Last-Modified` is known. The TTL
still governs *whether* we ask — this issue changes what the request costs, not how often it is made.

### 3. Handle `304 Not Modified`

**Status:** ⬜ Not started

Do not rewrite the cache file, do not re-run the converter, do not re-validate the schema. Reset the
freshness window so the TTL restarts, and report the outcome from step 4.

### 4. Add `SourceRefreshOutcome.Unchanged`

**Status:** ⬜ Not started

Distinct from `UpToDate`; conflating them loses the signal this issue exists to produce.

- `UpToDate` — the TTL had not expired, so upstream was never contacted.
- `Unchanged` — upstream *was* contacted and confirmed the cached copy is current.

Per ADR 008, a persisted enum value needs its `CHECK` constraint, baseline and drift test updated in
the same migration — confirm whether this outcome is persisted anywhere before assuming it is not.

### 5. Handle `200 OK`

**Status:** ⬜ Not started

Store the new validators alongside the newly written cache file, then continue through the existing
convert/validate/promote path unchanged.

### 6. Fall back cleanly when a server supplies no validators

**Status:** ⬜ Not started

A full GET, treated as `Updated`. No hash comparison, no heuristics — if upstream will not tell us
whether content changed, we fetch rather than guess. Log the case so a source that cannot support the
optimisation is visible rather than silently slow.

### 7. Decide `LastRefreshedAtUtc`'s meaning deliberately

**Status:** ⬜ Not started

It currently reports the cache file's own mtime and is documented as "how old the cached copy actually
is". If step 3 resets the freshness window by touching that mtime, the field starts meaning "last
checked" instead.

Either keep the two timestamps separate, or change the field deliberately and update its XML doc and
every consumer. Not as a side effect.

### 8. Feed #329's download statistics

**Status:** ⬜ Not started

A 304 is the cheapest possible refresh and should appear as such — near-zero bytes, one attempt — so
the value of this optimisation is measurable rather than assumed.

If #329 has not landed, this step states what it will report rather than inventing a second statistics
shape.

### 9. Distinguish the three outcomes in the log

**Status:** ⬜ Not started

`updated ... from ...` stays for a real change; an unchanged result says so explicitly; an
untouched-because-not-stale result stays distinct from both.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | Stored validators round-trip through both the sidecar and the row | Unit test | `SourceCacheValidatorStoreTests.StoredValidators_RoundTripThroughSidecarAndRow` |
| 2 | ❌ | Validators whose stored content hash no longer matches the file are not sent | Unit test | `SourceCacheValidatorStoreTests.ValidatorsWhoseContentHashNoLongerMatchesTheFile_AreNotSent` |
| 3 | ❌ | A missing cached file means validators are not used | Unit test | `SourceCacheValidatorStoreTests.CachedFileMissing_ValidatorsAreNotUsed` |
| 4 | ❌ | A stale source with a stored ETag sends `If-None-Match` | Unit test | `ConditionalSourceRefreshTests.StaleWithStoredETag_SendsIfNoneMatch` |
| 5 | ❌ | With only `Last-Modified` stored, `If-Modified-Since` is sent | Unit test | `ConditionalSourceRefreshTests.StaleWithOnlyLastModified_SendsIfModifiedSince` |
| 6 | ❌ | A 304 leaves the cache file untouched and does not re-run the converter | Unit test | `ConditionalSourceRefreshTests.NotModifiedResponse_LeavesCacheFileUntouched`, `...NotModifiedResponse_DoesNotRerunTheConverter` |
| 7 | ❌ | A 304 reports `Unchanged`, not `UpToDate` | Unit test | `ConditionalSourceRefreshTests.NotModifiedResponse_ReportsUnchangedNotUpToDate` |
| 8 | ❌ | A 304 resets the freshness window | Unit test | `ConditionalSourceRefreshTests.NotModifiedResponse_ResetsTheFreshnessWindow` |
| 9 | ❌ | A changed response stores the new validators and converts | Unit test | `ConditionalSourceRefreshTests.ChangedResponse_StoresTheNewValidators_AndConverts` |
| 10 | ❌ | A response without validators falls back to an unconditional download | Unit test | `ConditionalSourceRefreshTests.ResponseWithoutValidators_FallsBackToUnconditionalDownload` |
| 11 | ❌ | A source not yet stale sends no request at all and reports `UpToDate` | Unit test | `ConditionalSourceRefreshTests.NotStaleYet_SendsNoRequestAtAll_AndReportsUpToDate` |
| 12 | ❌ | `LastRefreshedAtUtc`'s meaning is either preserved or changed deliberately with its doc and consumers | Live | Read the field's XML doc and every consumer; no silent meaning change in the diff |
| 13 | ❌ | A 304 appears in the download statistics as near-zero bytes and one attempt | Unit test | Assert against #329's statistics shape |
| 14 | ❌ | The log distinguishes updated, unchanged, and not-yet-stale | Live | T1 + T2: three refreshes, three distinguishable log lines |
