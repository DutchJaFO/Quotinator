// For capture-page.csx --script-file, on /notifications. Opens the detail dialog of the first row whose
// title contains the text in the URL fragment (#reseeded cleanly), never simply the first row with a detail
// control: rows written in the same second are listed in no fixed order, so "the first" is a different kind
// from one run to the next. Returns the dialog's rectangle, so the capture is the dialog, with the shape of
// its table: how many headings, body rows and footer rows, and whether every row has one cell per heading.
// The footer is read separately because its Total line is not a tbody row.
await new Promise(r => setTimeout(r, 800));
const match = decodeURIComponent(location.hash.slice(1));
const row = [...document.querySelectorAll('tbody tr')]
  .filter(r => r.querySelector('.notification-body'))
  .find(r => r.querySelector('.notification-title').textContent.includes(match));
row.querySelector('.notification-detail-open').click();
await new Promise(r => setTimeout(r, 1200));
const dialog = document.querySelector('.modal-dialog');
const t = document.querySelector('.modal-body .notification-detail-table');
const cells = (r) => r.querySelectorAll('td,th').length;
const headers = t.querySelectorAll('thead th').length;
const bodyRows = [...t.querySelectorAll('tbody tr')];
const footRows = [...t.querySelectorAll('tfoot tr')];
const b = dialog.getBoundingClientRect();
return {
  x: b.left + scrollX, y: b.top + scrollY, width: b.width, height: b.height,
  title: row.querySelector('.notification-title').textContent.trim(),
  headers,
  bodyRows: bodyRows.length,
  footRows: footRows.length,
  everyRowMatchesHeaders: [...bodyRows, ...footRows].every(r => cells(r) === headers),
};
