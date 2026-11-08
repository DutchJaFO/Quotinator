// For capture-page.csx --script-file. Lists which notification kinds the API holds and how many of each,
// as `kindList` ("announcement=1 reseedfileapplied=5"), sorted by kind, and how many of each are dismissed
// as `dismissedList`, so a document asserts the exact set in one value. Either reads `none` when empty. A kind named in the URL fragment (#whatsnew) is waited
// for, up to 30 seconds, because some producers run detached after startup.
const waitFor = decodeURIComponent(location.hash.slice(1));
const deadline = Date.now() + 30000;
let items = [];
for (;;) {
  items = (await (await fetch('/api/v1/notifications?pageSize=0')).json()).items;
  if (!waitFor || items.some(i => i.metadataKind === waitFor) || Date.now() > deadline) break;
  await new Promise(r => setTimeout(r, 1000));
}
const count = (list) => list.reduce((acc, i) => { acc[i.metadataKind] = (acc[i.metadataKind] ?? 0) + 1; return acc; }, {});
const list = (counts) => Object.keys(counts).sort().map(k => `${k}=${counts[k]}`).join(' ') || 'none';
const kinds = count(items);
const dismissed = count(items.filter(i => i.isDismissed));
return { kindList: list(kinds), dismissedList: list(dismissed), kinds, dismissed };
