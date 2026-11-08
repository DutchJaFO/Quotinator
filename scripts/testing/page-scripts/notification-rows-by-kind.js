// For capture-page.csx --script-file, on /notifications or on / (the startup popup). Reads every rendered
// notification row and pairs it with its kind through the API, by title, then summarises per kind: how
// many rows, how many render an empty body or carry their title inside the body, how many open a detail
// dialog or expand one in place, and the extra buttons a row offers beyond Details and Dismiss. A row is a
// table row holding a .notification-body cell, which excludes the rows of a detail table nested inside it.
await new Promise(r => setTimeout(r, 800));
const scope = location.pathname === '/' ? document.querySelector('.modal') : document;
const items = (await (await fetch('/api/v1/notifications?pageSize=0')).json()).items;
const kindOf = (title) => items.find(i => i.title === title)?.metadataKind ?? '(unknown)';
const rows = [...scope.querySelectorAll('tbody tr')].filter(r => r.querySelector('.notification-body'));
const byKind = {};
for (const r of rows) {
  const title = r.querySelector('.notification-title')?.textContent.trim() ?? '';
  const body = r.querySelector('.notification-body')?.textContent ?? '';
  const k = kindOf(title);
  const s = byKind[k] ??= { rows: 0, emptyBody: 0, titleInsideBody: 0, opensDetail: 0, expandsInPlace: 0, buttons: [], extraButtons: '' };
  s.rows++;
  if (body.trim().length === 0) s.emptyBody++;
  if (title && body.includes(title)) s.titleInsideBody++;
  if (r.querySelector('.notification-detail-open')) s.opensDetail++;
  if (r.querySelector('details.notification-detail')) s.expandsInPlace++;
  for (const b of r.querySelectorAll('button')) {
    const t = b.textContent.trim();
    if (t && t !== 'Details' && t !== 'Dismiss' && !s.buttons.includes(t)) s.buttons.push(t);
  }
}
for (const s of Object.values(byKind)) { s.extraButtons = s.buttons.join(', '); delete s.buttons; }
return { rows: rows.length, anyButton: rows.some(r => r.querySelectorAll('button').length > 0), byKind };
