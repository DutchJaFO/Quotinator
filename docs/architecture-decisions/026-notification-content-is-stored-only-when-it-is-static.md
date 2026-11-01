# ADR 026 — A notification's text is stored only when it is static; anything that depends on current state is composed from its payload when read

**Status:** Accepted
**Date:** 2026-11-01
**GitHub issues:** #304, #312, #319, #348, #478

---

## Context

A notification is written once and may be read months later. By then the settings may have changed, the
files on disk may be different, and the condition it describes may have moved. `Title` and `Body` hold
what was true when the row was written, in every language, and cannot follow any of that.

A notification also carries a payload (`Metadata`, typed by `MetadataKind`) and a set of actions. The
offered actions are already worked out when the notification is read, from the payload and
`NotificationActionAvailability`, so an action reflects the present while the sentence beside it can
describe the past. The two disagree, and an operator acts on the sentence.

## Decision

**1. The test is whether the text stays true.** Ask of each sentence: would this still be true if read
later, after settings, files or state had changed?

| Answer | Example | What is displayed |
|---|---|---|
| Yes: it records something that happened, or announces something fixed | A database was created; a migration applied; an operation id was renamed | The stored `Title` and `Body` |
| No: it describes what is true now, or what an action would do | What a reseed would load; what the operator can do next | Composed when read |

**2. Stored text needs no special handling.** A static notification is written with its translations and
resolved at read, as it always was. This is the default, and it stays the default: composing is for the
content that needs it.

**3. Composed content comes from the payload and current availability, and nothing else.** The payload
states the facts of the event; `NotificationActionAvailability` states what is possible now. The content
is built from those at read time, in the language requested, from keys in `i18ntext/UI.*.json`.

**4. A composed notification and its actions are decided from the same answer.** Text saying a thing can
be done and an action offering it read the same availability, so they cannot disagree. An action that
could not do what it says is not offered, and the content says why.

**5. The endpoint and the page show the same content, composed in one place.** `GET /api/v1/notifications`
and `NotificationTable` never compose separately.

**6. The payload holds facts and identity, not prose.** A value the content needs is a field on the
payload, so it can be read back and worded in any language. A sentence in the payload is the same frozen
text in a different column.

## Consequences

**A new notification kind answers the test when it is designed.** Most will be static, and need nothing
beyond what they already do.

**A composed notification's translations live in the UI string files**, where
`TranslationCompletenessTests` covers them, rather than on the row. It costs a read-time lookup of what
is possible now, which the offered actions already pay.

**Changing a composed notification's wording reaches rows already stored**, which is the point. Changing
a static one's does not, because a record of what happened keeps what it said.

## Known non-compliance

- **`ReseedRecommended`** displays stored text for both of its reasons, and says what a reseed would load
  without knowing. Tracked by #478.
- **Not yet assessed against the test:** `BackupQuotaReached` and `BackupMaxExceeded` state a folder size
  that can move while the notification stays open; `ImportReviewPending` states a count of changes that
  can fall as they are decided; `BackupRefused` and `SchemaVersionOvershoot` describe a condition that
  stays open across restarts. Each may prove acceptable as a statement of what was true when raised, but
  that is a decision to make, not one to assume.
