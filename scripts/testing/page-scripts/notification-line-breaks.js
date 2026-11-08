// For capture-page.csx --script-file. Finds the first notification body holding a line break and compares
// how many line boxes it renders under its own white-space rule with how many it renders under `normal`,
// which collapses breaks. More boxes under the page's own rule is what shows the breaks are honoured.
await new Promise(r => setTimeout(r, 800));
const cell = [...document.querySelectorAll('.notification-body')].find(c => c.textContent.includes('\n'));
const lineBoxes = (el) => { const r = document.createRange(); r.selectNodeContents(el); return r.getClientRects().length; };
const withOwnRule = lineBoxes(cell);
cell.style.whiteSpace = 'normal';
const withNormal = lineBoxes(cell);
cell.style.whiteSpace = '';
return { whiteSpace: getComputedStyle(cell).whiteSpace, breaksHonoured: withOwnRule > withNormal, withOwnRule, withNormal };
