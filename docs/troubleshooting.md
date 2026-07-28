# Troubleshooting

Common problems and recovery procedures for Quotinator running outside of the Home Assistant add-on (plain Docker or docker-compose). For HA add-on users, see `addon/DOCS.md`.

---

## Container fails to start after using Reset Database

**Affected versions:** v1.5.x – v1.6.1  
**Fixed in:** v1.6.2

Using the **Reset Database** admin action in v1.5.x – v1.6.1 can leave the database in a broken state where the container fails to start on every subsequent attempt. The error in the container log is:

```
SQLite Error 1: 'duplicate column name: ImportBatchId'
```

The reset clears the schema version history but does not drop the underlying tables. On every restart the container tries to re-apply the same migration and hits the same error.

To recover, choose the option that fits your situation.

### Option A — Restore from a host or volume snapshot (easiest, preserves everything)

If you take regular backups of your Docker volume or host data directory, restore from a snapshot taken before Reset Database was triggered. Stop the container first, restore the snapshot, then start it again.

```bash
docker stop quotinator
# restore your snapshot to the data directory, e.g.:
# rsync -a /backups/quotinator-data-2026-06-21/ /path/to/data/
docker start quotinator
```

### Option B — Restore from a database backup (preserves quotes)

Quotinator automatically creates a backup of the database before applying schema upgrades. A backup will exist if you installed Quotinator before the import-provenance feature was added (roughly v1.5.0).

> **Important:** the failed reset and any subsequent restart attempts also create a backup, but those capture the *broken* state and are not useful for recovery. Use the **oldest** backup, not the most recent one.

1. **Stop the container:**
   ```bash
   docker stop quotinator
   ```

2. **List backups on the host, oldest first.** The data directory on the host is whatever path you mounted to `/data` (see [`docs/docker.md`](docker.md#data-directory-and-volume-mounts) — never `/app/data`, which collides with the image's bundled sources):
   ```bash
   ls -lt /path/to/data/backups/ | tail -n +2 | tail -5
   ```
   Backups are named `quotinatordata_v{N}_{timestamp}Z.db`. If the only files there have today's date, they are the corrupted backups — use Option C instead.

3. **Restore the oldest backup:**
   ```bash
   cp /path/to/data/backups/quotinatordata_v2_<earliest-timestamp>Z.db \
      /path/to/data/quotinatordata.db
   ```

4. **Start the container:**
   ```bash
   docker start quotinator
   ```
   The container will detect schema version 2, apply the missing migration correctly, and reseed any missing data.

> The valid backup contains the database state from before the import-provenance migration. Quotes added after that original upgrade will need to be re-added.

### Option C — Delete the database (clean slate, loses all data)

Use this if no backup exists or if you do not need to preserve existing data. The container will reseed from the bundled source files on next start.

1. **Stop the container:**
   ```bash
   docker stop quotinator
   ```

2. **Delete the database file:**
   ```bash
   rm /path/to/data/quotinatordata.db
   ```

3. **Start the container:**
   ```bash
   docker start quotinator
   ```

> Quotes added via the import feature or manual edits are not part of the bundled source files and will be lost.

---

## Upgrading to v1.6.2 from a broken installation

If your container is stuck in the crash loop described above, upgrading to v1.6.2 also fixes the loop. Pull the new image and restart:

```bash
docker pull ghcr.io/dutchjafo/quotinator:latest
docker stop quotinator
docker rm quotinator
docker run -d \
  -p 8080:8080 \
  -v /path/to/data:/data \
  -e Quotinator__DataDir=/data \
  ghcr.io/dutchjafo/quotinator:latest
```

v1.6.2 detects the partially-applied migration state on startup, records the correct schema version, and continues. No manual database intervention is needed if you upgrade.
