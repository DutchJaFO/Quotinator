// For capture-page.csx --script-file. Waits up to ten seconds for an external link whose address contains
// the URL fragment (#no-backup-could-be-taken), and returns its address and text with the viewport height:
// a zero height would make every element read as off-screen.
const sleep = ms => new Promise(r => setTimeout(r, ms));
const wanted = decodeURIComponent(location.hash.slice(1));
let link = null;
for (let i = 0; i < 40 && !link; i++) {
  link = [...document.querySelectorAll('a[target=_blank]')].find(a => a.href.includes(wanted));
  if (!link) await sleep(250);
}
return { innerHeight: window.innerHeight, href: link?.href ?? null, text: link?.textContent.trim() ?? null };
