// For capture-page.csx --script-file. Reads the notifications of the kind named in the URL fragment
// (#backuprefused) from the API: how many are active, and the first active one's type, dismiss trigger and
// offered actions, so a document asserts one notification by its kind rather than by its position in a
// list whose order is not defined among rows written in the same second (#486).
const kind = decodeURIComponent(location.hash.slice(1));
const items = (await (await fetch('/api/v1/notifications?pageSize=0')).json()).items;
const active = items.filter(i => i.metadataKind === kind && !i.isDismissed);
const first = active[0];
return {
  active: active.length,
  type: first?.type ?? null,
  dismissTriggerKey: first?.dismissTriggerKey ?? null,
  availableActions: (first?.availableActions ?? []).join(','),
};
