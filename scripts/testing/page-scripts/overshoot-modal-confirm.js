// For capture-page.csx --script-file. Arms the startup modal's Reset, confirms it, waits up to fifteen
// seconds for the schema-ahead reason to leave the page, and reports whether it did.
const sleep = ms => new Promise(r => setTimeout(r, ms));
const modal = () => document.querySelector('.modal');
const btn = t => modal() ? [...modal().querySelectorAll('button')].find(b => b.textContent.trim() === t) : null;
for (let i = 0; i < 40 && !modal(); i++) await sleep(250);
btn('Reset the database').click(); await sleep(1500);
btn('Confirm').click();
for (let i = 0; i < 60 && document.body.innerText.includes('schema version is ahead'); i++) await sleep(250);
return { reasonGone: !document.body.innerText.includes('schema version is ahead') };
