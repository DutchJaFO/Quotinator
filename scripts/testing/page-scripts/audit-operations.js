// For capture-page.csx --script-file. Counts the audit trail's entries for the table named in the URL
// fragment, by operation, as `operations` ({"BackupSkipped": 1}), so a document asserts how many times an
// operation was recorded. The admin endpoint needs the key, which is the test profile's own, `smoketest`.
const table = decodeURIComponent(location.hash.slice(1));
const response = await fetch(`/api/v1/admin/audit?table=${encodeURIComponent(table)}&pageSize=0`, { headers: { 'X-Api-Key': 'smoketest' } });
const items = (await response.json()).items;
const operations = items.reduce((acc, i) => { acc[i.operation] = (acc[i.operation] ?? 0) + 1; return acc; }, {});
return { status: response.status, operations };
