// For capture-page.csx --script-file. Arms the startup modal's Reset, cancels it, and reports the
// modal's button labels before, while armed, and after the cancel.
const sleep = ms => new Promise(r => setTimeout(r, ms));
const modal = () => document.querySelector('.modal');
const btn = t => modal() ? [...modal().querySelectorAll('button')].find(b => b.textContent.trim() === t) : null;
const labels = () => [...modal().querySelectorAll('button')].map(b => b.textContent.trim()).join(', ');
for (let i = 0; i < 40 && !modal(); i++) await sleep(250);
const before = labels();
btn('Reset the database').click(); await sleep(1500);
const armed = labels();
btn('Cancel').click(); await sleep(1200);
return { before, armed, afterCancel: labels() };
