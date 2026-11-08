// For capture-page.csx --script-file, on /notifications. The URL fragment names a row and one of its
// options, separated by a bar (#Content was not loaded|Reseed without a backup). Clicks the option until
// its Confirm appears, since a click issued before the Blazor circuit connects is swallowed; reads what the
// row asks before anything runs; confirms; then waits, at most 120 seconds, for the page's running spinner
// to appear and go, which is the page itself saying the action has returned (#367).
const sleep = ms => new Promise(r => setTimeout(r, ms));
const [text, option] = decodeURIComponent(location.hash.slice(1)).split('|');
const row = () => [...document.querySelectorAll('tr')].find(r => r.textContent.includes(text));
const btn = t => [...(row()?.querySelectorAll('button') ?? [])].find(b => b.textContent.trim() === t);
for (let i = 0; i < 40 && !row(); i++) await sleep(250);
let tries = 0;
while (!btn('Confirm') && tries++ < 20) { btn(option)?.click(); await sleep(500); }
if (!btn('Confirm')) return { confirmOffered: false };
const asked = [...row().querySelectorAll('.form-text')].map(e => e.textContent.trim()).join(' / ');
const askedLinks = row().querySelectorAll('a[target=_blank]').length;
btn('Confirm').click();
let sawRunning = false, finished = false;
for (let i = 0; i < 240 && !finished; i++) {
  await sleep(500);
  const running = document.querySelector('.badge.bg-info .spinner-border') !== null;
  sawRunning = sawRunning || running;
  finished = sawRunning && !running;
}
return { confirmOffered: true, asked, askedLinks, sawRunning, finished };
