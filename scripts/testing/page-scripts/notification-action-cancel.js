// For capture-page.csx --script-file. Arms the Reset action on the notifications page, reads the buttons it
// offers, cancels, and reads them again.
const sleep = ms => new Promise(r => setTimeout(r, ms));
const row = () => [...document.querySelectorAll('table.table > tbody > tr')].find(r => r.textContent.includes('needing an action'));
const buttons = () => [...row().children[5].querySelectorAll('button')].map(b => b.textContent.trim());
const click = async label => {
  [...row().children[5].querySelectorAll('button')].find(b => b.textContent.trim() === label).click();
  await sleep(1000);
};
const before = buttons();
await click('Reset the database');
const armed = buttons();
await click('Cancel');
return { before, armed, afterCancel: buttons() };
