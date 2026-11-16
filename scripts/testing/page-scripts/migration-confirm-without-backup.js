// For capture-page.csx --script-file. In the startup modal, arms "Migrate without a backup", confirms it, waits up
// to thirty seconds for the refused-migration reason to leave the page, and reports whether it did (#490 step 17).
const sleep = ms => new Promise(r => setTimeout(r, ms));
const modal = () => document.querySelector('.modal');
const btn = t => modal() ? [...modal().querySelectorAll('button')].find(b => b.textContent.trim() === t) : null;
const refused = () => document.body.innerText.includes('Database migration refused');
for (let i = 0; i < 40 && !btn('Migrate without a backup'); i++) await sleep(250);
btn('Migrate without a backup').click(); await sleep(1500);
btn('Confirm').click();
for (let i = 0; i < 120 && refused(); i++) await sleep(250);
return { reasonGone: !refused() };
