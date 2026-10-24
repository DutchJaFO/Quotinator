# Every page shows an error after startup, while /health says the data directory cannot be written

**Kind:** diagnostic
**Entry code:** —
**Affected versions:** 1.9.0-alpha onwards
**GitHub issue:** #454, #455, #332

## Symptom

`/api/v1/health` answers `503`:

```
{"status":"unhealthy","reason":"The data directory cannot be written. This usually means the volume is mounted read-only, or the container user lacks write permission on it. Restore write access to the data directory and restart. A database Reset cannot resolve this: it writes too."}
```

and every page, `/`, `/stats` and `/notifications`, answers `500` instead of the *Quotinator started
with a problem* page. The log carries, once per page request:

```
Microsoft.AspNetCore.DataProtection.KeyManagement.KeyRingProvider[{ Id: 48, Name: "ErrorOccurredWhileReadingKeyRing" }] An error occurred while reading the key ring.
```

## Does it prevent the app or API from functioning?

**Yes.** The application is degraded: every endpoint that reads the database answers `503`, and the
pages that would explain why cannot render. The API reference at `/scalar/v1` and the spec at
`/openapi/v1.json` still answer.

## Cause

The `keys/` folder inside the data directory, where the key ring the pages depend on is kept, could not
be created or written at startup. Three causes produce it:

- **A file named `keys` occupies the path** in the data directory, so no folder can be created there.
  The rest of the data directory may be perfectly writable, and the database may be healthy; the
  `/health` reason still names the data directory as a whole.
- **The whole data directory cannot be written or created**, for example a volume mounted read-only, or
  a configured data directory below a read-only mount.
- **The `keys` folder exists but cannot be written**, for example a separate read-only mount at `keys/`,
  or a folder whose permissions deny the container user. The rest of the data directory may be writable.

## Remedy

Look at the data directory from outside the container.

- **If a file named `keys` is there**, remove it (or move it aside), then restart. The application
  creates the folder and its key ring itself.
- **If the `keys` folder exists**, give the container user write access to it (or remount it writable),
  then restart.
- **Otherwise**, restore write access to the data directory: remount the volume writable, or correct
  the container user's permissions on it, then restart.

A database Reset does not help with any of these causes. With the first cause it runs, and can even turn
`/health` healthy, but the pages keep failing until the `keys` path is free.

## Workaround: keep the key ring in memory

Where the `keys` folder cannot be made writable, the key ring can live in memory instead. Two ways:

- **A memory-backed mount, in plain Docker.** Mount an in-memory filesystem over the folder, for example
  `docker run ... -v <data>:/data --tmpfs /data/keys ...`. The application sees a writable `keys/`, starts
  healthy and the pages render. This is not available in the Home Assistant add-on, whose mounts the
  supervisor decides; its `tmpfs` option covers `/tmp` only.
- **An in-memory key ring inside the application.** Not available yet: whether, and when, Quotinator falls
  back to it is [#332](https://github.com/DutchJaFO/Quotinator/issues/332)'s fallback chain for the keys
  location.

**What it costs.** The key ring is lost on every restart, so each browser's existing cookies and
antiforgery tokens stop decrypting. Each browser logs one pair of errors on its first page after the
restart and is then given a fresh cookie; see
[An antiforgery token could not be decrypted](antiforgery-token-could-not-be-decrypted.md). Whether
anything worse follows, such as an open page losing its connection across a restart, has not been
measured. Prefer fixing the folder; use this when that is not possible.

## Notes

Found by #327's `startup-and-degradation/10` and `11` (2026-10-23). #454 tracks making the pages render
their degraded form in this state, as they do for every other startup problem; #455 tracks the reason
naming the keys folder rather than the data directory, and a Reset no longer clearing the fault.
[#332](https://github.com/DutchJaFO/Quotinator/issues/332) plans a fallback chain for the keys location,
so the pages keep working when the preferred location cannot be written.
