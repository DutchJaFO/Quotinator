##### *GENERATED FILE [2026-08-12 03:59 UTC] — do not edit by hand.*

# Changelog

All notable changes to this add-on will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/).

## [1.8.3-beta2] - 2026-08-12

- Fixed a crash in the degraded-database status pages (introduced in the previous beta) that could happen when upgrading from an older database version.
- Fixed an issue that could cause a Home Assistant add-on upgrade to fail partway through.

---

## [1.8.3-beta] - 2026-08-10

- If the database ever fails to start up correctly, Quotinator now stays reachable and reports the problem clearly instead of crashing or showing a technical error page — an admin database Reset can fix it without needing to restart the app or container.
- Quotinator now keeps a copy of every import/seed file it processes — including which conversion settings were used — so the original content can be listed, reviewed, or downloaded again later, and old versions can be pruned to reclaim space.
- Old conflict-resolution history from completed imports is now cleaned up automatically, and the full audit trail (what changed and when) can be exported in bulk for record-keeping.
- Resetting the database is now a complete, unconditional wipe — it always rebuilds from scratch and no longer automatically re-imports the bundled quote content afterward. Nothing survives a reset any more, including the audit trail, so export it first (see above) if you want to keep it.
- When something goes wrong at startup, the web interface itself now shows a clear popup explaining what happened and the database's last known-good status — not just the REST API. Home is disabled until the problem is resolved, but the REST API (clearly marked as limited), Statistics, and About pages stay reachable. Database status — quote counts and which files were used to build it — is also now always available on a new Statistics page in the navigation menu.
- A new Notifications page and startup popups now surface informational, warning, error, and action-recommended messages from Quotinator, with a history you can review and dismiss at any time.
- Breaking change for API clients: two REST endpoints' operation IDs were renamed for naming consistency — `GetImportBatches` is now `GetAllImportBatches`, and `GetFileResources` is now `GetAllFileResources`. This only affects a generated API client keyed by operation ID; the routes and behaviour themselves are unchanged.
- Opening Quotinator while it's still setting up the database (a fresh install, or a large re-seed) no longer shows a blank, unresponsive page — a "Quotinator is starting up" page now appears immediately and refreshes itself automatically until the app is ready.

---

## [1.8.2] - 2026-07-31

- Security: a SQLite library vulnerability (CVE-2025-6965) has now been fixed directly by upgrading the affected native library; no user data was affected.

---

Older releases are available in the full history on GitHub: https://github.com/DutchJaFO/Quotinator/releases
