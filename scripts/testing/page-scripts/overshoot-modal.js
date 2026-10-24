// For capture-page.csx --script-file. Reads the startup modal of an application whose schema version
// is ahead of the build: whether it renders, what its reason names, and how many notification rows it
// shows. Substring matches rather than regex literals, so nothing in the text is read as a path.
const modal = document.querySelector('.modal');
const text = (modal ? modal.innerText : '').toLowerCase();
return {
  modal: !!modal,
  namesTheOvershoot: text.includes('schema version is ahead'),
  namesReset: text.includes('reset'),
  namesRestore: text.includes('restore a backup'),
  notificationRows: modal ? modal.querySelectorAll('.notification-body').length : -1
};
