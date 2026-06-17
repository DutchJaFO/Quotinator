# #68 — Curated JSON: conversations format

**Status:** Not started  
**GitHub issue:** #68  
**Depends on:** #67 (schema), #61 (per-source file format)

---

## Spec requirements

1. Source files support an **extended object format** in addition to the current flat array:
   ```json
   {
     "quotes": [ ... ],
     "conversations": [ ... ],
     "stageDirections": [ ... ],
     "soundCues": [ ... ]
   }
   ```
   Flat array files remain valid — the parser must handle both.

2. `quotinator-curated.json` migrated from flat array to the extended object format.

3. `schemas/source-extended.schema.json` updated with `conversations`, `stageDirections`, and `soundCues` sections.

4. `DatabaseInitializer` seeds `Conversations`, `ConversationLines`, `StageDirections`, `StageDirectionTranslations`, `SoundCues`, `SoundCueTranslations` from the new sections.

5. The Airplane! (1980) conversation added to `quotinator-curated.json` as a representative example:
   - Characters: Roger Murdock, Joey, Ted Striker
   - "Have you ever been in a cockpit before?" — "No sir, I've never been up in a plane before." — "You ever seen a grown man naked?" …

6. `SeedScriptIntegrityTests` updated to validate the extended format.

---

## Implementation steps

- [ ] Update `SeedBatch` / source file parser to detect flat array vs extended object format
- [ ] Parse `conversations`, `stageDirections`, `soundCues` sections from extended format
- [ ] Update `DatabaseInitializer` to seed conversation tables when extended format is present
- [ ] Migrate `data/sources/quotinator-curated.json` from flat array to extended object format
- [ ] Add the Airplane! conversation to `quotinator-curated.json`
- [ ] Update `schemas/source-extended.schema.json` with new sections
- [ ] Update `seed.csx` to validate source files against the schema
- [ ] Update `SeedScriptIntegrityTests` to cover the extended format
- [ ] Tests: extended-format file seeds conversation tables correctly; flat array still seeds correctly

---

## Notes

The flat array format remains valid for source files that have no conversations. Only `quotinator-curated.json` needs to move to the extended format initially (it is the only source with manually curated entries that would have conversations).

The Airplane! conversation is the canonical test case from the spec — use it as the first real conversation entry.
