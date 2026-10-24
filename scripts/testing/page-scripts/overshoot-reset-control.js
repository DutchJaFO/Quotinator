// For capture-page.csx --script-file. Waits up to ten seconds for a "Reset the database" button, then
// reports how many there are and whether the schema-ahead reason is shown.
const sleep = ms => new Promise(r => setTimeout(r, ms));
const resets = () => [...document.querySelectorAll('button')].filter(b => b.textContent.trim() === 'Reset the database').length;
for (let i = 0; i < 40 && resets() === 0; i++) await sleep(250);
return { resetButtons: resets(), reasonShown: document.body.innerText.includes('schema version is ahead') };
