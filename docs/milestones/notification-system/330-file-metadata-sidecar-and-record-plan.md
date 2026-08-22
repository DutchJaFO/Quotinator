# #330 — File metadata: sidecar and database record for every file we create or inspect

**Status:** Planning
**GitHub issue:** #330
**Tiers required:** T1, T2
**Depends on:** none

---

## Description

Quotinator writes and reads files it does not own the contents of — bundled sources, the manifest,
downloaded source caches, user imports — and keeps no per-file record of having looked at them. There
is no answer to "have we inspected this file, and what did it look like when we did".

`Import_FileResource` (#251/#252) is adjacent but answers a different question. It is keyed by
*content version*: one row per distinct SHA-256, deduplicated so re-capturing unchanged content
updates `LastSeenAtUtc`. That answers "have we ever seen these bytes", not "what do we currently know
about this file", and it carries no MD5 and no place for out-of-band metadata such as HTTP validators.

This issue establishes the missing half: a per-file record, created the first time a file is
downloaded or inspected, held both as a sidecar beside the file and as a row in a new table.

**Decisions taken with the developer before filing (2026-08-19):** a new file-keyed table referencing
`Import_FileResource` rather than an extension of it; the manifest gets both a sidecar and a row
("applies to all files we create" wins); conditional requests are #331's, not part of this.

---

## Steps

### 1. Check the design against the governing ADRs before writing anything

**Status:** ⬜ Not started

Not against a neighbouring entity — `CLAUDE.md` records how copying the previous entity's shape
propagated an ADR 002 violation three times before anyone checked.

- [ADR 015](../../architecture-decisions/015-domain-prefixed-table-naming.md) — domain-prefixed,
  singular. Proposed `Import_FileMetadata`, in the `Import_` domain alongside the table it references.
  (`Metadata` as one word matches existing usage — `MetadataKind`,
  `NotificationLegacyMetadataMigrations`.)
- [ADR 002](../../architecture-decisions/002-recordbase-on-all-tables.md) — `FileMetadataEntity`
  derives from `RecordBase`, without exception.
- [ADR 016](../../architecture-decisions/016-class-naming-suffixes-and-enum-placement.md) — `Entity`
  suffix; any new enum in its own `Enums/` folder.
- [ADR 012](../../architecture-decisions/012-canonicalize-entity-ids-at-capture.md) — natural-key
  comparison via `TextClauses.Equals`, never hand-written `LOWER(x) = LOWER(y)`.

### 2. Define the columns and the natural key

**Status:** ⬜ Not started

Beyond `RecordBase`: `HomeDirectoryKey` (the named root, symbolic — never an expanded `{dataDir}`
path, matching `Import_FileResource`'s existing scheme), `FileName` (plain, no path segments),
`ContentHash` (SHA-256, lowercase hex), `Md5Hash`, `SizeBytes`, `FileResourceId` (nullable FK to the
content-version row when one exists), `FirstInspectedAtUtc`, `LastInspectedAtUtc`.

`(HomeDirectoryKey, FileName)` is the natural key and must be unique, compared case-insensitively.

### 3. Document why each hash exists

**Status:** ⬜ Not started

SHA-256 is the canonical content hash and the one `Import_FileResource` already dedupes on. MD5 exists
for interoperability with services that publish MD5 as their own integrity value.

**MD5 must carry no security claim anywhere in the code or docs, and must never be the hash a
correctness decision is made on.** State that in the XML docs, not just here.

### 4. Write the sidecar

**Status:** ⬜ Not started

`<filename>.meta.json` beside every file we create or inspect, carrying the same content hash, MD5,
size and inspection timestamps as the row. **The manifest gets one too.**

Read and written through a DTO with `[JsonPropertyName]` per this project's JSON parsing policy —
never a hand-assembled `JsonObject`, never manual node-walking on read.

### 5. Establish the record at first download or first inspection

**Status:** ⬜ Not started

During manifest creation. A file that has been through this path once has, by definition, been
inspected at least once — which is the guarantee this issue exists to provide. Re-inspecting an
unchanged file updates `LastInspectedAtUtc` in place; it never inserts a second row and never rewrites
an identical sidecar.

### 6. Implement the reconciliation rule

**Status:** ⬜ Not started

State it and implement it rather than letting it emerge. The file on disk is the only thing that
cannot be stale — its bytes are the truth; the sidecar and the row are records *about* those bytes and
either can be absent or out of date.

- Sidecar present and its hash matches the file → trusted, no rehash.
- Sidecar missing, stale, or unparseable → recompute from the file, rewrite the sidecar, update the row.
- Row missing but sidecar valid → recreate the row from the sidecar. Since Reset is a full wipe with no
  protected tables (#156), this is the normal path after a Reset, not an edge case.
- An unparseable sidecar is replaced, never a hard failure. Log it.

### 7. Add the migration and update the baseline in the same commit

**Status:** ⬜ Not started

Appended to `DatabaseInitializer.DataOwnedMigrations` — never reordered or edited afterwards — with
the baseline SQL updated to match its final result and the schema-drift parity test covering the new
table. Any enum-backed column carries a `CHECK` constraint enumerating its members per
[ADR 008](../../architecture-decisions/008-enum-backed-columns-require-check-constraints.md).

### 8. Put all SQL in `Sql.cs` under the project's own rules

**Status:** ⬜ Not started

A new nested class, with column names from `nameof(FileMetadataEntity.X)` and the table name held in
one `private const string Table`. Every selected `*Id` column goes through `IdClauses.SelectColumn` —
`SqlSelectPresentationGuard` and `SqlIdCaseGuard` enforce this mechanically, so a miss is a test
failure rather than a review miss.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | Baseline and incremental replay produce an identical schema for the new table | Unit test | `FileMetadataMigrationTests.Baseline_And_IncrementalReplay_ProduceIdenticalSchema` |
| 2 | ❌ | `(HomeDirectoryKey, FileName)` is unique | Unit test | `FileMetadataMigrationTests.NaturalKey_IsUniqueAcrossHomeDirectoryKeyAndFileName` |
| 3 | ❌ | First inspection inserts a row carrying both hashes | Unit test | `FileMetadataRepositoryTests.FirstInspection_InsertsRowWithBothHashes` |
| 4 | ❌ | Re-inspecting an unchanged file updates `LastInspectedAtUtc` only | Unit test | `FileMetadataRepositoryTests.ReInspectingUnchangedFile_UpdatesLastInspectedOnly` |
| 5 | ❌ | Filename lookup is case-insensitive | Unit test | `FileMetadataRepositoryTests.FileNameLookup_IsCaseInsensitive` |
| 6 | ❌ | A captured content version links to its `Import_FileResource` row | Unit test | `FileMetadataRepositoryTests.ContentVersionCaptured_LinksToTheFileResourceRow` |
| 7 | ❌ | First inspection writes a sidecar beside the file | Unit test | `FileMetadataSidecarTests.FirstInspection_WritesSidecarBesideTheFile` |
| 8 | ❌ | The manifest gets a sidecar and a row | Unit test | `FileMetadataSidecarTests.ManifestFile_AlsoGetsASidecarAndARow` |
| 9 | ❌ | A sidecar matching the file is trusted without rehashing | Unit test | `FileMetadataSidecarTests.SidecarMatchingTheFile_IsTrustedWithoutRehashing` |
| 10 | ❌ | A stale sidecar is recomputed and rewritten | Unit test | `FileMetadataSidecarTests.SidecarStale_IsRecomputedAndRewritten` |
| 11 | ❌ | An unparseable sidecar is replaced and logged, never thrown | Unit test | `FileMetadataSidecarTests.SidecarUnparseable_IsReplacedAndLogged_NotThrown` |
| 12 | ❌ | A missing row is recreated from a valid sidecar | Unit test | `FileMetadataSidecarTests.RowMissingButSidecarValid_RowIsRecreatedFromSidecar` |
| 13 | ❌ | MD5 carries no security claim and no correctness decision anywhere | Live | XML docs state it; no code branches on `Md5Hash` |
| 14 | ❌ | All SQL sits in `Sql.cs` with `nameof` column names and wrapped id columns | Unit test | `SqlQueryGuardTests`, `SqlIdCaseGuard`, `SqlSelectPresentationGuard` |
| 15 | ❌ | Sidecars and rows exist for real files after a live run, including the manifest | Live | T1 + T2: inspect the data directory and the table after startup |
