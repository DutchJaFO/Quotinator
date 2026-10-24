// For capture-page.csx --script-file. Counts the notifications page's rows and body cells, and reports
// whether its empty state is shown.
return {
  rows: document.querySelectorAll('tbody tr').length,
  bodyCells: document.querySelectorAll('.notification-body').length,
  emptyState: document.body.innerText.toLowerCase().includes('no notifications yet')
};
