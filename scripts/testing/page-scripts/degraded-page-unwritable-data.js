// For capture-page.csx --script-file. Reports, for a page rendered while the data directory cannot be
// written: whether the reason is shown, whether a stack trace reached the page, whether Blazor's error
// banner is visible, and how many requests failed with anything other than the gate's 503.
const text = (document.querySelector('main') || document.body).innerText;
const errorUi = document.getElementById('blazor-error-ui');
return {
  reasonShown: text.includes('data directory cannot be written'),
  stackTrace: /at [A-Za-z.]+\(/.test(document.body.innerText),
  errorUiShown: errorUi ? getComputedStyle(errorUi).display !== 'none' : null,
  failuresOtherThan503: performance.getEntriesByType('resource').filter(e => e.responseStatus >= 400 && e.responseStatus !== 503).length
};
