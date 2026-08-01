##### *GENERATED FILE [2026-08-01 09:36 UTC] — do not edit by hand.*

# Changelog

All notable changes to Quotinator are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added
- Release entries can now carry an optional one-line `quote` (release-note flavour text, with optional `attribution`) — rendered in `CHANGELOG.md` and the Blazor changelog UI, omitted from the more concise `addon/CHANGELOG.md`/`addon-beta/CHANGELOG.md` (issue #178)
- OpenAPI/Scalar documentation now publishes real typed response schemas for the quote endpoints (`/quotes/random`, `/search`, `/{id}`, `/quotes`) and the admin endpoints (`/admin/database/seed/preview`, `/reseed`, `/reset`, `/admin/sources/refresh`, `/admin/audit`) — previously every one of these showed only a bare `200 OK` with no schema (issue #148)

### Changed
- Documented that the database's internal audit-trail tables (event/audit history) intentionally retain their record after the entity they describe is later changed or replaced — a purely internal documentation clarification with no behaviour change (issue #151)
- Investigated the OS-level vulnerabilities Docker Scout reports in the container's base image; a routine rebuild resolved nearly all of them, and the remainder are tracked as accepted residual risk with no fix currently available upstream — no application change (issue #232)

### Fixed
- Database columns backed by the duplicate-resolution-policy setting (`ImportBatches.ConflictPolicy`, and the internal `AppliedPolicy` column on two provenance tables) now reject an invalid value at the database level via a CHECK constraint, matching every other enum-backed column in the schema; a pre-existing data inconsistency this closed is also normalised automatically (issue #150)
- The test suite's build now surfaces outdated MSTest assertion patterns (e.g. `CollectionAssert`/`StringAssert` instead of the modern `Assert` equivalents) as visible warnings instead of silently allowing them to accumulate; around 2,700 existing occurrences across the test suite were also modernised in the same change (issue #197)
- The release process no longer publishes a Home Assistant add-on version update until its matching Docker image is confirmed available, closing a window where installing or updating the add-on right after a new version appeared could fail (issue #236)

---

## [1.8.2] - 2026-07-31

### Highlights
- Security: a SQLite library vulnerability (CVE-2025-6965) has now been fixed directly by upgrading the affected native library; no user data was affected.

### Fixed
- Upgraded the native SQLite library (`SQLitePCLRaw.lib.e_sqlite3`) to `3.50.3` in every affected project via a direct package override, resolving CVE-2025-6965 (aggregate query memory corruption in SQLite versions before 3.50.2) directly instead of relying only on the existing query-shape mitigation (issue #72)

---

## [1.8.1-beta] - 2026-07-30

### Highlights
- A separate Beta add-on is now available in Home Assistant, letting you try upcoming changes without affecting your stable installation — install both side by side.
- Changelogs shown in the app and the Home Assistant add-on now list only the most recent releases, with a link to the full history on GitHub.

### Added
- New `addon-beta/` Home Assistant add-on definition (slug `quotinator_beta`) publishes the beta channel as a separate, always-visible, independently installable add-on from the same repository and Docker image as the stable add-on (issue #166)
- `scripts/changelog.csx` gained a `--max-releases <N>` option limiting generated changelog output to the N most recent releases, with a closing note linking to the full history on GitHub Releases (issue #166)

### Changed
- `CHANGELOG.md`, `addon/CHANGELOG.md`, and the new `addon-beta/CHANGELOG.md` are now generated with `--max-releases 3`, showing only the 3 most recent releases instead of the full history (issue #166)
- A beta tag now only bumps `addon-beta/config.yaml`'s version, and a final tag only bumps `addon/config.yaml`'s — previously one shared file toggled between the two states (issue #166)

---

## [1.8.0] - 2026-07-29

### Highlights
- Security: an OpenAPI documentation library vulnerability (CVE-2026-49451) was identified and fixed; the vulnerable code path was never reachable in Quotinator, and no user data was affected.
- Setting up Quotinator for the first time is now faster — a brand-new database is created directly, instead of stepping through years of internal upgrade history.
- Quotinator can now automatically refresh its bundled and imported quote sources from their original locations, keeping data up to date without needing a new release.
- Duplicate quotes found during import can now be merged field by field instead of only being kept or replaced outright, and every duplicate encountered is now recorded for future review.
- The default behaviour for duplicate quotes changed from keeping the first version seen to keeping the newest version — matching what most people would expect when a source file is corrected or updated.
- Quotes can now be imported one file at a time (JSON or CSV) through a new API endpoint, with a preview mode that shows exactly what would happen before anything is written.
- Duplicate quotes flagged for manual review can now be resolved field by field, choosing which side wins or supplying your own value — changes only take effect once every conflict from the same import file has been decided.
- An import that needs review can now be finished later by referencing its batch, without needing to re-upload the file.
- An applied import can now be undone through a new endpoint — reversing everything it added or changed, as long as no newer import has happened since.
- Adding a new quote source in CSV or JSON format usually no longer requires writing any code — a manifest entry with simple field-mapping options is now enough for most formats.
- Multi-line exchanges — a back-and-forth between characters, with stage directions and sound cues in between — can now be fetched as a single ordered conversation, and a random quote that's part of one no longer shows up on its own without the rest of the exchange.
- A source's title, type, or release date can now be corrected after the fact — for example fixing a typo in a film title, or filling in a missing year — without creating a duplicate entry.
- Stage directions and sound cues used in multi-line conversations can now be corrected after the fact too, the same way a source's details already could.
- A conversation's description can now be corrected after the fact too; the lines that make up the conversation itself are unaffected.
- A person's name, date of birth, and date of death can now be corrected after the fact too; date of birth and date of death can now actually be set for a person for the first time.
- Quotes are now correctly grouped by the film series or fictional universe they belong to, and can be filtered by series or universe when searching, listing, or requesting a random one.
- Sources, characters, people, series, universes, stage directions, and sound cues can now all be listed and looked up individually through new endpoints, the same way quotes already could.
- Correcting a source, person, stage direction, sound cue, or conversation by re-importing it now only changes the fields the file actually mentions — a field left out is no longer silently reset to blank.
- Pagination is now consistent across every list endpoint: requesting more than 500 items at once is rejected instead of silently limited, and requesting every matching item as one page (`pageSize=0`) works the same way everywhere.
- Most films, shows, and other sources shown by quotes now include a release date — previously this was almost always left blank.
- A character who appears across multiple entries in the same series (like a recurring character in a trilogy) is now recognised as the same character instead of being tracked separately for each entry, as long as they're the same type of media.
- A character's name can now be corrected after the fact too, the same way a source's, person's, stage direction's, sound cue's, and conversation's details already could.
- A series's or universe's name (and a series's linked universe) can now be corrected after the fact too, the same way a source's, person's, character's, stage direction's, sound cue's, and conversation's details already could.
- Quotes waiting for review after an import can now be resolved all at once by exporting them to a file, editing the decisions, and importing the file back — instead of deciding each one individually through the API.
- Known, recurring conflicts between bundled quote sources are now resolved automatically on import instead of requiring the same manual decision every time the data is reprocessed.
- A number of duplicate entries caused by inconsistent movie titles across bundled data sources have been merged into one accurate entry each — including every Star Wars film, several Marvel films, The Godfather Part II, and Creed II, among others.
- James Bond has been added as a supported film franchise, alongside The Matrix.
- Rule files that correct recurring, known data conflicts can now be viewed, generated from a decision already made, and removed through new endpoints, instead of only being hand-edited.
- A few more duplicate entries (Airplane!, When Harry Met Sally, and an Avengers film) have been merged into one accurate entry each, and several quotes that were missing which character said them now have that filled in.
- A data-correction feature meant to fill in a missing detail (like which character said a quote) had never actually worked the first time a quote was seen — only on a later re-check — so any such correction made so far silently had no effect; this is now fixed.
- Importing, previewing an import, and reseeding now report exactly what happened per file and per type of data (new, corrected, held for review, etc.) instead of one vague duplicates number.

### Added
- A `manifest.json` is now auto-created in the user imports folder when one is missing, listing discovered files alphabetically; controlled by the `Quotinator__CreateMissingManifest` config key (default `true`)
- Manifest source entries can now declare a `github` object (`owner`, `repo`, `path`, `branch`) instead of a plain `url` — the provenance link and a fetchable raw-file download link are both derived from it automatically
- `Quotinator__IncludeDefaultSources` config key (default `true`) — when set to `false`, the bundled sources are skipped entirely, useful for a fully custom data setup
- `Quotinator__ImportsPath` config key (default `{DataDir}/imports`) — overrides where the user imports folder is scanned from
- A startup warning is now logged when the legacy `Quotinator__DataPath` environment variable is still set, pointing users to `Quotinator__DataDir` instead
- `POST /api/v1/admin/database/reset` now accepts a `preserveSchemaVersion` query parameter (default `false`) to keep existing schema migration history instead of clearing and replaying it
- New `Quotinator__AutoUpdateSources` (default `true`) and `Quotinator__SourceUpdateIntervalHours` (default `24`) config keys control automatic refreshing of manifest-declared sources with a `downloadUrl`
- Manifest entries can now declare `refreshIntervalHours` and `downloadTarget` to override the global refresh interval and cache location on a per-source basis
- New `POST /api/v1/admin/sources/refresh` endpoint refreshes downloaded source caches on disk without touching the database
- `POST /api/v1/admin/database/reseed` and `.../reset` now accept a `forceSourceRefresh` query parameter to bypass the refresh interval for that call
- `GET /api/v1/admin/database/seed/preview` and `POST /api/v1/admin/sources/refresh` now report each source's refresh outcome, last-refreshed time, and — for a file that failed to parse — a localised reason
- Five configurable duplicate-resolution policies — `skip`, `newest-wins`, `merge-ours`, `merge-theirs`, `review` — set via `Quotinator__DefaultConflictPolicy`, with per-entity-type overrides and a per-source manifest override
- New `System_ImportActions` table records every action a staged import or seed run would take, not only genuine duplicates, so a staged batch can be reviewed, decided, and applied or discarded before anything is written
- `ImportBatches` now records which conflict-resolution policy was active for each import batch
- New `POST /api/v1/import` endpoint imports a single source file (JSON, or CSV via a new converter plugin), reusing the same duplicate-detection engine as startup seeding — supports a per-request `duplicateResolution` override and an optional `converter` selection
- New `POST /api/v1/import/preview` endpoint runs the identical import pipeline but rolls back every write, so conflicts and errors can be reviewed before committing
- Manifest file entries (`data/sources/manifest.json` and user import manifests) can now declare their own `duplicateResolution` override, taking priority over the manifest-wide and configured defaults
- Quotes, sources, characters, people, conversations, stage directions, and sound cues now have a completeness status (not yet reviewed / looks complete / confirmed complete) and a `NoValueKnown` list of confirmed-empty fields in the database, laying the groundwork for future data-quality tooling; not yet exposed via the API or management UI beyond the import decide flow below, and never reset when an existing record is rewritten by a duplicate-resolution policy
- Deciding a staged import action can now also set the affected record's completeness status directly (most commonly confirming it's fully reviewed), applied together with the rest of that decision
- A record confirmed fully reviewed can no longer be silently overwritten by a later import — any attempt is held for explicit review instead, and holds the entire import batch (not just the affected record) until resolved
- Source records declared in a source file's `sources` section can now carry their own stable identifier, decoupling which row an import matches from that row's title/type/date — so a later correction updates the existing record instead of creating a duplicate
- `POST /api/v1/import/actions/{id}/decide` now also accepts decisions for a staged Source correction (title, type, date), not only quotes
- A new internal change log records every quote, source, and character created or modified during seeding and import, including which import batch introduced it — laying the groundwork for a future change-history view; not yet exposed via the API or management UI
- New `GET /api/v1/import/actions` endpoint lists staged import actions (quotes, sources, characters, people), paginated, filterable by status, import batch, and entity type — showing which fields still need a decision and how related actions in the same batch connect to each other
- New `POST /api/v1/import/actions/{id}/decide` and `.../undo` endpoints stage or revert a per-field keep/replace/custom-value decision for one staged action
- New `POST /api/v1/import/actions/apply` endpoint applies every decided action in a batch at once, atomically, once every one of them has a decision recorded
- New `POST /api/v1/import/actions/discard` endpoint discards every staged action in a batch at once, writing nothing
- `POST /api/v1/import` can now apply an already-staged batch directly by referencing its batch id, instead of always requiring the file to be uploaded again
- New `POST /api/v1/import/actions/reverse` endpoint undoes every action in an applied import batch — reversing an Add soft-deletes the record it created, reversing a Modify restores the pre-change field values; only the most recently applied batch still live can be reversed, and a `?preview=true` mode validates without writing anything
- The OpenAPI spec now documents `type`, `field`, `status`, and `entityType` query parameters as proper enums with their allowed values, instead of unconstrained strings
- New `basic-json-array` and `regex-array` converter plugins handle a flat JSON object array or a JSON array of regex-extractable strings respectively, both fully configurable via a manifest entry's `converterOptions` — most new sources no longer need a dedicated converter project
- The `csv` converter plugin now supports `converterOptions` (`columnMapping`, `hasHeader`, `defaults`) for CSV files whose header labels don't match Quotinator's own field names, or that have no header at all — previously only exact-matching header names were supported
- New database tables lay the groundwork for multi-line conversations — `Conversations`, `ConversationLines`, `StageDirections`, `SoundCues`, and their translation tables — allowing quotes, stage directions, and sound cues to be grouped into an ordered exchange; not yet populated by any source file or exposed via the API (issue #67)
- Source files can now define reusable stage directions, sound cues, and conversations grouping them together with quotes in order — seeded at startup and importable via `POST /api/v1/import` (JSON only) the same way quotes are, including duplicate detection and undo; not yet exposed via a read endpoint (issue #68)
- New `GET /api/v1/conversations/{id}` endpoint returns a full ordered conversation — quotes, stage directions, and sound cues in sequence, respecting `?lang=` with fallback to the original language (issue #69)
- Quote responses now include a `conversations` field listing which conversation(s) a quote belongs to, its position, and the conversation's total line count; omitted entirely for a quote that belongs to no conversation (issue #69)
- `GET /api/v1/quotes/random` now embeds the full conversation when a selected quote belongs to one, and excludes every other quote from that same conversation for the rest of the request; the response now reports `requestedCount` and `returnedCount` so a shortfall caused by this deduplication is visible (issue #69)
- `stageDirections[]`/`soundCues[]` entries with an id matching an existing row can now stage a `Modify` action instead of only ever being added once — the same policy-resolved diff, completeness-guard blocking, and decide/reverse workflow Source corrections already use
- `conversations[]` entries with an id matching an existing row can now stage a `Modify` action for their `description` field only — `lines` are never diffed, read, or written by this path, that remains a separate, not-yet-scoped future issue
- `people[]` entries with an id matching an existing row can now stage a `Modify` action instead of only ever being added once — the same policy-resolved diff and completeness-guard blocking Source/StageDirection/SoundCue/Conversation corrections already use; this is also the first path that ever writes a Person's `dateOfBirth`/`dateOfDeath` fields, previously always left `null`
- Source files can now declare `series[]`/`universe[]` sections linking related Sources into a franchise hierarchy (film → series → universe); a Source resolves its series by name
- A curated overlay file links each bundled film to its correct series/universe where one is known (e.g. the Lord of the Rings trilogy) — staged for review like any other correction, not applied automatically
- Quote responses, and the random/search/list endpoints' filters, now include the source's series and universe (if any), and can be filtered by `series`, `seriesId`, `universe`, or `universeId`
- New `GET /api/v1/masterdata/sources` and `.../sources/{id}` endpoints list and look up sources individually, including their linked series
- New `GET /api/v1/masterdata/characters` and `.../characters/{id}` endpoints list and look up characters individually, including every source they appear in
- New `GET /api/v1/masterdata/people` and `.../people/{id}` endpoints list and look up people individually
- New `GET /api/v1/masterdata/series` and `.../series/{id}` endpoints list and look up series individually, including their parent universe
- New `GET /api/v1/masterdata/universes` and `.../universes/{id}` endpoints list and look up universes individually
- New `GET /api/v1/conversations` endpoint lists conversations, paginated
- New `GET /api/v1/masterdata/stagedirections` and `.../stagedirections/{id}` endpoints list and look up stage directions individually
- New `GET /api/v1/masterdata/soundcues` and `.../soundcues/{id}` endpoints list and look up sound cues individually
- `characters[]` entries can now stage a `Modify` action instead of only ever being added once — matched by an optional explicit id, or resolved via the same type-anchored, series-scoped matching algorithm used to recognise a character across sources, using a new `sourceTitle`/`sourceType` pair on the entry; the same policy-resolved diff and completeness-guard blocking every other correctable entity already uses (issue #175)
- `series[]` and `universe[]` entries can now stage a `Modify` action instead of only ever being added once — matched by an optional explicit id, the same policy-resolved diff and completeness-guard blocking every other correctable entity already uses (issue #163)
- New `GET /api/v1/import/actions/export` endpoint exports every field awaiting a decision in a staged import batch — including ones currently held for review — as a CSV or JSON file (issue #163)
- New `POST /api/v1/import/actions/bulk-decide` endpoint accepts an edited export file back and applies every decision it contains in one call, reporting any row that couldn't be applied without affecting the rest of the file (issue #163)
- New per-source conflict-resolution rule file (`ruleFile` manifest property) lets a known, recurring field disagreement between two occurrences of the same record auto-resolve under the `review` policy instead of always staging for manual decision (issue #181)
- New per-source title-alias file (`sourceAliasFile` manifest property) corrects a misspelled or inconsistent Source title/type to its canonical form before it's matched against existing data — applies to both a brand-new record and a re-imported one, preventing a duplicate Source from ever being created for the same real film/show under a different spelling (issue #181)
- New `GET`/`POST /generate`/`DELETE /api/v1/import/rules/conflict` endpoints view, generate from a decided batch, and remove a persisted override of a per-source conflict-resolution rule file, without needing to hand-edit and redeploy it (issue #153)
- New `GET /api/v1/import/rules/alias` endpoint scans existing Sources for likely duplicate titles not yet covered by a title-alias file and suggests them for review — never writes an alias entry itself (issue #153)
- A staged action can now be flagged `Stale` when the conflict-resolution or title-alias rule that would have auto-resolved it no longer matches current data — held for review instead of silently reapplying an outdated correction (issue #153)
- `POST /api/v1/import`, `.../import/preview`, `POST /api/v1/admin/database/reseed`/`reset`, and `GET /api/v1/admin/database/seed/preview` now all return a per-file, per-entity-type report (new/modified/blocked/discarded/pending/stale counts) instead of a single flat `duplicates` count — replaces `LastSeedDuplicates`/`SeedDuplicateRecord` entirely (issue #221)
- `IDatabaseInitializer` now also exposes `SeriesCount`/`UniverseCount`/`StageDirectionCount`/`SoundCueCount`/`ConversationCount` alongside the existing four counts, surfaced in the startup `[Database - Stats]` log line and the `reseed`/`reset` endpoint responses (issue #221)

### Changed
- A brand-new database now creates its schema in one step instead of replaying every historical upgrade step in sequence; existing databases are unaffected and continue upgrading incrementally as before
- API responses no longer include properties with a `null` value, reducing response payload size
- The default duplicate-resolution policy changed from `skip` (keep the first version seen) to `newest-wins` (keep the latest version) when nothing overrides it
- Internal audit and duplicate-conflict records now carry the same creation/modification tracking as every other database record, for consistency; existing installations upgrade automatically on next startup with no action needed
- Import endpoints (`POST /api/v1/import`, `.../import/preview`) moved out from under `/api/v1/quotes` into their own `/api/v1/import` route group, all under a new `Import` OpenAPI tag
- `POST /api/v1/import` and `.../import/preview` now return `200` when everything applies (or would apply) cleanly, or `202` when any row needs a decision, instead of always returning `200`
- `GET /api/v1/import/actions`'s `status` filter now also accepts `blocked`, for an action held because it would have modified a record confirmed fully reviewed
- `POST /api/v1/import/preview` now stages a real, inspectable batch instead of rolling back its writes — nothing is written either way, but the staged batch can be reviewed afterward via `GET /api/v1/import/actions?batchId=`
- The bundled `NikhilNamal17/popular-movie-quotes` and `vilaboim/movie-quotes` sources now use the new generic `basic-json-array`/`regex-array` converters, configured via `converterOptions` in `data/sources/manifest.json` instead of dedicated per-source code — output is unchanged, same quote ids and content
- Every list endpoint's pagination now follows one consistent contract: `pageSize` above 500 is rejected (422) instead of silently capped, `pageSize=0` returns every matching row as a single page, and requesting a page past the last one is a distinct 422
- The default page size for `GET /api/v1/admin/audit` and `GET /api/v1/import/actions` changed from 50 to 20, matching every other list endpoint
- Character identity is no longer scoped to a single source: two characters with the same name are now treated as the same character when their sources share both media type and a known series; existing per-source duplicates are consolidated automatically on upgrade wherever a series relationship is known (issue #174)
- The startup banner's database statistics are now listed one per line under a new `Statistics:` section instead of crammed onto a single line, so additional entity types stay readable as more are added (issue #221)
- Formalised the project's existing C#-only tooling convention as a written architecture decision record — a purely internal documentation addition with no behaviour change (issue #159)

### Fixed
- ImportBatch rows created during seeding now record the correct `Type` (`Seed` for any bundled file, whether externally sourced with a manifest URL or internally authored) and persist the source URL; previously every seeded batch was recorded incorrectly
- Seeding no longer crashes on an empty or otherwise invalid JSON source file — the file is now skipped with a logged warning instead of stopping startup
- A file placed in the user imports folder with no URL was previously misclassified the same as internally-curated data; ImportBatch provenance now has a distinct `UserSeed` type for imports-folder files, separate from `Seed` (any bundled dataset, our own or internally authored)
- CVE-2026-49451: `Microsoft.OpenApi` (transitive via `Microsoft.AspNetCore.OpenApi`) had a stack-overflow vulnerability when parsing OpenAPI documents with circular schema references; Quotinator only generates its own OpenAPI document and never parses untrusted ones, so the vulnerable path was unreachable — patched to 2.7.5 via a direct package override regardless
- A full database reset (`POST /api/v1/admin/database/reset`) no longer wipes the audit log; the audit table (now named `System_AuditEntries`) always survives a reset, matching the behaviour a normal reseed already had. Internal tables essential to the app now use a `System_` name prefix so they are automatically protected from a reset, rather than the app needing to know each protected table by name
- Resetting the database now takes a safety backup first and automatically restores it if the reset fails partway through, instead of potentially leaving the database in a broken, half-rebuilt state
- The OpenAPI spec now correctly marks every import and staged-action-review write endpoint as requiring `X-Api-Key`; previously only `Admin`-tagged endpoints showed this requirement, so these never appeared as protected in the Scalar UI
- `status`, `entityType`, and `batchId` query filters on `GET /api/v1/import/actions` matched case-sensitively, so a lowercase value (e.g. `?status=pending`) silently returned no results even though matching data existed
- `POST /api/v1/import` with no file, no settings, and no batch id returned an uninformative generic error instead of a clear message stating that either a file or a batch id is required
- Re-importing or reseeding content that had previously been soft-deleted (via undo, or otherwise) silently failed to restore it — the record and its related rows are now properly resurrected instead of being permanently hidden behind the old row
- `GET /api/v1/quotes`'s `yearFrom`/`yearTo`/`year`/`decade` filters were documented as `integer` in the OpenAPI spec, but the schema patch never actually applied to this specific endpoint due to a route-path mismatch — the Scalar UI showed them as plain `string`; request handling itself was unaffected, this was a documentation-accuracy bug only
- The legacy in-memory `QuoteService` duplicated the source-file parser's logic instead of reusing it, and broke once a source file used the extended object format; now reuses the shared parser. This code path is not reachable in the running application — nothing has registered it since the SQLite migration — but it remains covered by its own test suite (issue #68)
- Quote, source, character, and conversation SQL queries had been living inside the generic data-access library instead of the Quotinator-specific project since before that split existed — a purely internal code-organisation fix with no behaviour change (issue #157)
- Import-batch tracking (which file was imported, when, and by what policy) had also ended up in the Quotinator-specific project instead of the generic data-access library it actually belongs in — another purely internal code-organisation fix with no behaviour change (issue #158)
- A quote confirmed fully reviewed could still have its fields silently changed by a later import — only a Source correction was actually held for review as intended; both are now correctly held. A Source correction under a policy that keeps the existing value on conflict no longer holds unnecessarily when nothing would actually change (issue #168)
- `ImportActionNotDecidableException`'s message and the `POST /import/actions/{id}/decide` API description named `Quote`/`Source` specifically and claimed other entity types were always already-decided — both were stale since Source's own correction path shipped; reworded generically so the message stays accurate as more entity types become correctable
- `page`/`pageSize` query parameters were documented as `string` instead of `integer` in the OpenAPI spec for every list endpoint that has them — continuing the same fix already applied to `/quotes`'s year filters, now covering pagination everywhere it's used
- An import file correcting a source, person, stage direction, sound cue, or conversation could silently reset a field to blank just by not mentioning it in the file — an omitted field now correctly leaves the existing value untouched; an explicit `null` still resets it as intended
- Generic list-endpoint infrastructure (pagination, not-found handling, masterdata routing/tagging conventions, entity-scoped filter parsing) is now shared across every list endpoint instead of being reimplemented per entity — a purely internal consolidation, no behaviour change beyond the pagination contract change above (issues #193, #196)
- `Quotinator.Engine` — a third internal project that sat between the API and the generic data-access library — has been merged into `Quotinator.Core`; a purely internal code-organisation change with no behaviour change (issue #206)
- A source's release date was silently dropped whenever the source was only discovered from a quote instead of being explicitly declared in a source file's own `sources[]` section — the common case for almost every bundled source; the date is now correctly recorded. Sources already seeded before this fix are unaffected until a full database reset (issue #191)
- A source, person, stage direction, sound cue, or conversation created with a lowercase explicit id could resolve correctly through one lookup path (for example, via a related quote) but fail to be found by that same record's own individual lookup endpoint — ids are now normalised consistently wherever they're written, fixing the inconsistency (issue #209)
- A quote created with an uppercase explicit id could resolve correctly through most lookups, but `GET /api/v1/quotes/{id}` only matched when the URL's casing exactly matched the id as originally imported — ids are now normalised consistently for quotes too, closing the last remaining case of this issue (issue #210)
- Batch and record ids shown in `GET /api/v1/import/actions` and `GET /api/v1/admin/audit` responses could appear in a different letter casing than every other id in the same response — they're now always shown consistently, regardless of how they were originally stored (issue #210)
- A newly-created person's date of birth and date of death were silently discarded even when the import file supplied them — only a correction to an already-existing person actually saved these dates; both are now saved correctly the first time (issue #173)
- Internal import-batch bookkeeping queries read every column implicitly instead of by name, the one remaining gap in the id-consistency work above — closed with no other observable change, since the affected id already displayed correctly (issue #212)
- An internal import-batch bookkeeping field didn't follow the naming pattern the id-consistency work above relies on to find and protect id columns automatically — renamed for consistency; the field was not yet used anywhere, so there is no other observable change (issue #213)
- The internal test suite's automatic id-consistency checks previously relied on developers remembering to list certain internal query-building methods by hand; they are now discovered and checked automatically instead — a purely internal test-coverage improvement with no user-facing or behaviour change (issue #214)
- A related internal query-joining mechanism was only automatically checked for one of the three internal safety checks the rest of the codebase applies everywhere else; it is now checked for all three, closing the last remaining gap of this kind — a purely internal test-coverage improvement with no user-facing or behaviour change (issue #215)
- Matching a source by its title and type during import was case-sensitive, so re-importing the same source with different letter casing (e.g. from a differently-formatted file) could create a duplicate instead of updating the existing one; matching is now case-insensitive, consistent with how every other identifier in the system is already matched (issue #175)
- An import finished through the staged review-and-decide workflow (`POST /api/v1/import/actions/apply`) was never recorded as applied, so undoing it afterwards (`POST /api/v1/import/actions/reverse`) always failed even though the import itself had succeeded; the batch is now correctly marked applied and can be undone like any other (issue #177)
- Several bundled Sources existed as duplicate rows for the same real film due to inconsistent title spelling across data sources (e.g. every Star Wars episode, several Marvel films, The Godfather Part II, Creed II) — consolidated to one canonical Source each (issue #181)
- A Source was resolved from a quote's raw incoming title/type before any conflict-resolution rule had a chance to run, so a rule correcting a Quote's own displayed field never actually prevented — and in one case (Zootopia) actively caused — a duplicate Source row under the uncorrected spelling; Source resolution now consults the new title-alias mechanism first (issue #181)
- `PlanSourcesAsync` (the Source-correction planning path) had never been wired to the per-source conflict-resolution rule file mechanism, so a legitimate cross-file Source enrichment (e.g. a curated quote establishing a Source, a later file assigning it to a series) had no way to auto-resolve under the `review` policy and always required manual decision (issue #181)
- Filtering quotes by `series=`/`universe=` name (rather than id), matching a Series/Universe/Person by name during import, the `lang` query parameter, and `GET`/`DELETE /api/v1/admin/audit`'s `table` filter all matched case-sensitively — a lowercase or differently-cased value (e.g. `?universe=james bond` against a stored `"James Bond"`) silently returned no results, or in the `DELETE` case silently deleted nothing while still reporting success; all four now match case-insensitively, consistent with every other identifier in the system (issue #216)
- Case-insensitive matching for Source titles, Series/Universe/Person names, and a few internal status filters was implemented as separate hand-written SQL in each place, and one of them (import-action status/entity-type filtering) had drifted onto the opposite letter-case convention from everywhere else; consolidated onto one shared, tested mechanism with no change to actual matching behaviour, plus an automated check that catches a future comparison of this kind if it's ever added without the same case-insensitive handling (issue #211)
- A conflict-resolution rule's `Custom` value (filling in a field that's missing or wrong on both sides, e.g. which character said a quote) only ever applied the second time the same quote was encountered, never the first — so it silently never took effect for a quote appearing exactly once anywhere in the bundled data, which is the common case; it now applies from the first encounter (issue #153)
- Fixing the above exposed two further issues in the same mechanism: a field already correctly resolved could be incorrectly held for review as if the rule governing it had gone stale, and a record needing no correction at all (every field already agreeing) could be held for review forever instead of resolving immediately; both are now fixed (issue #153)
- Airplane!, When Harry Met Sally, and an Avengers film each existed as duplicate entries under an inconsistently-spelled title in one bundled data source — merged into their single existing, correctly-spelled entry (issue #153)
- A database upgrading directly from v1.7.2 could have had some internal-table migrations silently skipped, due to a bookkeeping bug in how the old combined migration-version number was split into the newer internal counters — corrected before ever reaching a release (issue #155)
- Every internal migration step added since v1.7.2 that has not yet reached a release has been consolidated into two atomic steps instead of many small ones, and verified end-to-end against a real v1.7.2 database — a purely internal simplification with no schema or behaviour change (issue #155)
- A route/query-derived filename, origin, and batch id were logged in two internal rule-file code paths without stripping embedded newlines, letting a caller forge fake log lines in the plain-text log output (CWE-117) — the same defensive fix already applied to request logging in v1.7.1 was extended to both call sites and centralised into one shared helper
- Several endpoints built an error-message detail via `string.Format` on a localised template — since which of the three translation files is consulted depends on the request's own `Accept-Language` header, a future translation-file placeholder-count typo could have thrown an unhandled exception; localised message substitution now uses a dedicated method that never throws instead (CWE-134)
- A directory-traversal guard for internal rule-file paths relied on .NET's own filename parsing, which only recognises a backslash as a path separator on Windows — on Linux, the platform this project actually ships on, a backslash-containing filename could bypass the guard (not actually exploitable as a real traversal there, since backslash isn't a path separator on Linux either); the guard now rejects both separator characters explicitly, regardless of platform
- `GET /api/v1/version`'s database stats only reported the original four entity counts (quotes, sources, characters, people) — the five newer counts added alongside them (series, universes, stage directions, sound cues, conversations), already surfaced in the startup log and the reseed/reset endpoint responses, are now included here too

### Removed
- The `nikhilnamal17` and `vilaboim` converter plugin names no longer exist — a custom manifest entry referencing either by name must be updated to `basic-json-array`/`regex-array` with the equivalent `converterOptions`

---

Older releases are available in the full history on [GitHub Releases](https://github.com/DutchJaFO/Quotinator/releases).

[Unreleased]: https://github.com/DutchJaFO/Quotinator/compare/v1.8.2...HEAD
[1.8.2]: https://github.com/DutchJaFO/Quotinator/compare/v1.8.1-beta...v1.8.2
[1.8.1-beta]: https://github.com/DutchJaFO/Quotinator/compare/v1.8.0...v1.8.1-beta
[1.8.0]: https://github.com/DutchJaFO/Quotinator/compare/v1.7.2...v1.8.0
