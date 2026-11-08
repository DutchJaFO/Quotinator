// For capture-page.csx --script-file, on /notifications or on / (the startup popup). Waits up to ten
// seconds for the table row whose text contains the URL fragment (#Content was not loaded), and returns
// its rectangle, so the capture is the row, with its buttons, the external links it carries, and the
// viewport height: a zero height would make every element read as off-screen.
const sleep = ms => new Promise(r => setTimeout(r, ms));
const text = decodeURIComponent(location.hash.slice(1));
let row = null;
for (let i = 0; i < 40 && !row; i++) {
  row = [...document.querySelectorAll('tr')].find(r => r.textContent.includes(text));
  if (!row) await sleep(250);
}
if (!row) return { found: false, innerHeight: window.innerHeight };
const r = row.getBoundingClientRect();
return {
  found: true,
  innerHeight: window.innerHeight,
  buttons: [...row.querySelectorAll('button')].map(b => b.textContent.trim()).join(' | '),
  buttonCount: row.querySelectorAll('button').length,
  links: [...row.querySelectorAll('a[target=_blank]')].map(a => a.href).join(' '),
  x: r.x + scrollX, y: r.y + scrollY, width: r.width, height: r.height,
};
