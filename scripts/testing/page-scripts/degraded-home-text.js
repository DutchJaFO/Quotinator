// For capture-page.csx --script-file. Reports whether the page says the application started with a
// problem, and whether any stack trace reached the page.
return {
  degradedTextPresent: document.body.innerText.includes('started with a problem'),
  stackTracePresent: /at [A-Za-z.]+\(/.test(document.body.innerText)
};
