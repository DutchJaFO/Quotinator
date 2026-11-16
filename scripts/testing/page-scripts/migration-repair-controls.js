// For capture-page.csx --script-file. Waits up to ten seconds for the degraded page's migration repair controls
// (#490 step 17), then reports how many "Resume the migration" and "Migrate without a backup" buttons it shows.
const sleep = ms => new Promise(r => setTimeout(r, ms));
const count = t => [...document.querySelectorAll('button')].filter(b => b.textContent.trim() === t).length;
for (let i = 0; i < 40 && count('Resume the migration') === 0; i++) await sleep(250);
return { resumeButtons: count('Resume the migration'), withoutBackupButtons: count('Migrate without a backup') };
