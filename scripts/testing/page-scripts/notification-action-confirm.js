// For capture-page.csx --script-file. Arms the Reset action on the notifications page, confirms it, and
// waits up to fifteen seconds for its row to leave the page.
const sleep = ms => new Promise(r => setTimeout(r, ms));
const row = () => [...document.querySelectorAll('table.table > tbody > tr')].find(r => r.textContent.includes('needing an action'));
const click = async label => {
  [...row().children[5].querySelectorAll('button')].find(b => b.textContent.trim() === label).click();
  await sleep(1000);
};
await click('Reset the database');
await click('Confirm');
for (let i = 0; i < 60 && row(); i++) await sleep(250);
return { rowGone: !row() };
