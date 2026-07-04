# #67 — Conversations schema

**Status:** Not started  
**GitHub issue:** #67  
**Depends on:** #58 (ImportBatch FK on new tables)  
**Unblocks:** #68, #69

---

## Spec requirements

New tables to support multi-line conversational quotes:

### `Conversations`
`Id` (UUID PK), `QuoteId` (UUID FK → Quotes), `ImportBatchId` (UUID FK → ImportBatches, nullable)

### `ConversationLines`
`Id` (UUID PK), `ConversationId` (UUID FK → Conversations), `Position` (INTEGER), `CharacterId` (UUID FK → Characters, nullable), `Text` (TEXT), `Language` (TEXT, ISO 639-1)  
CHECK constraint: `Position >= 1`

### `StageDirections`
`Id` (UUID PK), `ConversationId` (UUID FK → Conversations), `AfterLinePosition` (INTEGER nullable — NULL = before first line), `Text` (TEXT)

### `StageDirectionTranslations`
`Id` (UUID PK), `StageDirectionId` (UUID FK → StageDirections), `Language` (TEXT), `Text` (TEXT)

### `SoundCues`
`Id` (UUID PK), `ConversationId` (UUID FK → Conversations), `AtLinePosition` (INTEGER nullable), `Description` (TEXT)

### `SoundCueTranslations`
`Id` (UUID PK), `SoundCueId` (UUID FK → SoundCues), `Language` (TEXT), `Description` (TEXT)

Indexes on all FK columns.

---

## Implementation steps

1. [ ] Schema migration: create all six tables with FK constraints and indexes (version bump)
2. [ ] `Conversation`, `ConversationLine`, `StageDirection`, `StageDirectionTranslation`, `SoundCue`, `SoundCueTranslation` C# records in `Quotinator.Core`
3. [ ] CHECK constraint on `ConversationLines.Position >= 1`
4. [ ] Repository interfaces and Dapper implementations in `Quotinator.Data`
5. [ ] Tests: schema created correctly, FK constraints enforced, CHECK constraint enforced, indexes present

---

## Notes

A `Conversation` belongs to a `Quote`. A Quote can have at most one Conversation (1:1 or 0:1). The `ConversationLines` are the individual spoken lines in order. `StageDirections` are non-spoken annotations (e.g. `[drinks coffee]`). `SoundCues` are audio annotations (e.g. `[gunshot]`).

Both `StageDirections` and `SoundCues` have `AfterLinePosition` / `AtLinePosition` as nullable integers: NULL means before the first line (position 0), an integer N means after line N.
