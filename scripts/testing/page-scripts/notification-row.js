// For capture-page.csx --script-file, on /notifications or on / (the startup popup). Finds the first
// notification row whose title contains the text in the URL fragment, and returns that row's rectangle, so
// the capture is cropped to the row itself, with what the row offers. A fragment ending in "|expand"
// (#needs review|expand) opens the row's in-place detail first, so the image shows it expanded.
await new Promise(r => setTimeout(r, 800));
const [match, mode] = decodeURIComponent(location.hash.slice(1)).split('|');
const scope = location.pathname === '/' ? document.querySelector('.modal') : document;
const rows = [...scope.querySelectorAll('tbody tr')].filter(r => r.querySelector('.notification-body'));
const row = rows.find(r => r.querySelector('.notification-title').textContent.includes(match));
if (mode === 'expand') {
  const d = row.querySelector('details.notification-detail');
  if (d) d.open = true;
  await new Promise(r => setTimeout(r, 300));
}
const b = row.getBoundingClientRect();
return {
  x: b.left + scrollX, y: b.top + scrollY, width: b.width, height: b.height,
  title: row.querySelector('.notification-title').textContent.trim(),
  opensDetail: !!row.querySelector('.notification-detail-open'),
  expandsInPlace: !!row.querySelector('details.notification-detail'),
  buttons: [...row.querySelectorAll('button')].map(x => x.textContent.trim()).join(', '),
};
