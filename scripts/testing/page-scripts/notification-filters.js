// For capture-page.csx --script-file. Reads the notifications page under each filter, naming the status
// of each of the rows constructed-notifications.sql inserts.
const sleep = ms => new Promise(r => setTimeout(r, ms));
const rows = () => [...document.querySelectorAll('table.table > tbody > tr')];
const body = row => row.querySelector('.notification-body')?.textContent ?? '';
const status = part => {
  const row = rows().find(r => body(r).includes(part));
  return row ? row.children[4].textContent.trim() : null;
};
const buttons = () => rows().flatMap(r => [...r.children[5].querySelectorAll('button')].map(b => b.textContent.trim()));
const filter = async label => {
  [...document.querySelectorAll('[role=group] button')].find(b => b.textContent.trim() === label).click();
  await sleep(1000);
};
const activeSelected = [...document.querySelectorAll('[role=group] button')].find(b => b.textContent.trim() === 'Active').className.includes('btn-primary');
const active = { rows: rows().length, actionRowListed: status('needing an action') !== null, actionButtons: buttons() };
await filter('All');
const all = {
  rows: rows().length,
  action: status('needing an action'),
  expired: status('has already expired'),
  dismissed: status('already dismissed'),
  obsolete: status('no longer exists')
};
await filter('Expired only');
const expiredOnly = { rows: rows().length, expiredRowListed: status('has already expired') !== null };
return { activeSelected, active, all, expiredOnly };
