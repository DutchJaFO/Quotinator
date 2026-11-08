// For capture-page.csx --script-file. Waits up to ten seconds for the notifications table, then returns how
// many rows carry the reserve caution and the buttons those rows offer, with the viewport height: a zero
// height would make every element read as off-screen.
const sleep = ms => new Promise(r => setTimeout(r, ms));
let rows = [];
for (let i = 0; i < 40 && rows.length === 0; i++) {
  rows = [...document.querySelectorAll('tbody tr')];
  if (rows.length === 0) await sleep(250);
}
const cautioned = rows.filter(r => r.textContent.includes('comes from the reserve'));
return {
  innerHeight: window.innerHeight,
  rows: rows.length,
  cautioned: cautioned.length,
  buttons: cautioned.flatMap(r => [...r.querySelectorAll('button')].map(b => b.textContent.trim()))
};
