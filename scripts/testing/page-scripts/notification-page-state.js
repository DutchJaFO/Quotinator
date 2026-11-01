// For capture-page.csx --script-file. Reads what the notifications page or the home page renders: the
// table's own rows, not the ones nested inside a row's detail, and whether each thing a document asserts
// on is there. Counts elements rather than matching body text, because a section's name also appears in
// the page's own navigation.
const text = document.body.innerText;
const main = document.querySelector('main') ?? document.body;
return {
  title: document.title,
  rows: document.querySelectorAll('table.table > tbody > tr').length,
  bodyCells: document.querySelectorAll('.notification-body').length,
  announcementShown: text.includes('Two REST API operation IDs were renamed'),
  emptyFilterShown: text.includes('No notifications match this filter.'),
  readyShown: text.includes('Quotinator is ready') && text.includes('Startup completed successfully.'),
  statsShown: document.querySelectorAll('dd').length > 0,
  notificationsHeadingInMain: [...main.querySelectorAll('h1,h2,h3,h4,h5,h6')].some(h => h.textContent.trim() === 'Notifications'),
  stackTraceShown: /at [A-Za-z.]+\(/.test(text)
};
