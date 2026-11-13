// For capture-page.csx --script-file. Reads every DatabaseActionResult notification (#348) from the API: how
// many there are, and the newest one's type and payload, so a document asserts the result an action
// recorded. Each action run from the application writes one, in its own second, so newest-first is defined
// across steps even though rows written in the same second have no defined order (#486).
const items = (await (await fetch('/api/v1/notifications?pageSize=0')).json()).items;
const results = items
  .filter(i => i.metadataKind === 'databaseactionresult')
  .sort((a, b) => String(b.createdAt).localeCompare(String(a.createdAt)));
const newest = results[0];
const payload = newest?.metadata ? JSON.parse(newest.metadata) : {};
return {
  count: results.length,
  type: newest?.type ?? null,
  title: newest?.title ?? null,
  body: newest?.body ?? null,
  action: payload.action ?? null,
  outcome: payload.outcome ?? null,
  restorePoint: payload.restorePoint ?? null,
  fileName: payload.fileName ?? null,
  backupObstacle: payload.backupObstacle ?? null,
};
