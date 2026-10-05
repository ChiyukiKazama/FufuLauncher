const element = id => document.getElementById(id);
let currentUid = '';
let busy = false;
const checkedIds = () => [...document.querySelectorAll('#devices input:checked')].map(input => input.value);
function status(text) { element('status').textContent = text; }
function setBusy(value) {
  busy = value;
  document.querySelectorAll('button').forEach(button => { button.disabled = value; });
}
async function api(path, options = {}) {
  const response = await fetch(path, { ...options, cache: 'no-store', headers: {
    'Authorization': `Bearer ${element('admin-key').value}`, 'Content-Type': 'application/json'
  } });
  if (response.status === 401) throw new Error('管理密钥不正确。');
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(error.error || `请求失败（${response.status}）`);
  }
  return response.status === 204 ? null : response.json();
}
async function load(uid) {
  const devices = await api(`/api/admin/devices?uid=${encodeURIComponent(uid)}`);
  currentUid = uid;
  element('devices').replaceChildren();
  element('results').hidden = false;
  element('actions').hidden = devices.length === 0;
  element('select-all').checked = false;
  element('result-title').textContent = `UID ${uid} · ${devices.length} 台设备`;
  for (const device of devices) {
    const row = document.createElement('div'); row.className = 'device';
    const input = document.createElement('input'); input.type = 'checkbox'; input.value = device.motherboardId;
    input.setAttribute('aria-label', `选择主板 ${device.motherboardId}`);
    const detail = document.createElement('div');
    const id = document.createElement('div'); id.className = 'device-id'; id.textContent = device.motherboardId;
    const meta = document.createElement('p'); meta.className = 'meta';
    meta.textContent = `首次上报：${new Date(device.firstReportedAt).toLocaleString()}　最近上报：${new Date(device.lastReportedAt).toLocaleString()}`;
    const badge = document.createElement('span'); badge.className = `badge${device.banned ? ' banned' : ''}`;
    badge.textContent = device.banned ? `已限制 · 到期 ${new Date(device.expiresAt).toLocaleString()}` : '插件可用';
    detail.append(id, meta, badge);
    if (device.reason) { const reason = document.createElement('p'); reason.textContent = device.reason; detail.append(reason); }
    row.append(input, detail); element('devices').append(row);
  }
  status(devices.length ? '请核实关联记录，再复选需要操作的设备。' : '没有关联记录。设备需使用接入此服务的启动器并同意上报后，才能出现在这里。');
}
element('search-form').addEventListener('submit', async event => {
  event.preventDefault(); if (busy) return;
  setBusy(true);
  try { await load(element('uid').value.trim()); } catch (error) { status(error.message); element('results').hidden = true; }
  finally { setBusy(false); }
});
element('select-all').addEventListener('change', event => {
  document.querySelectorAll('#devices input').forEach(input => { input.checked = event.target.checked; });
});
async function update(banned) {
  if (busy) return;
  const ids = checkedIds(); if (!ids.length) { status('请先选择设备。'); return; }
  const reason = element('reason').value.trim(); const durationDays = Number(element('days').value);
  if (banned && (!reason || !Number.isInteger(durationDays) || durationDays < 1 || durationDays > 90)) {
    status('请填写限制原因，期限为 1–90 天。'); return;
  }
  if (!confirm(`${banned ? '限制' : '解除限制'} ${ids.length} 台设备的插件注入？`)) return;
  setBusy(true);
  try {
    await api('/api/admin/bans', { method: 'POST', body: JSON.stringify({ motherboardIds: ids, banned, reason, durationDays }) });
    await load(currentUid); status(banned ? '所选设备已限制，下一次启用插件时生效。' : '所选设备的限制已解除。');
  } catch (error) { status(error.message); } finally { setBusy(false); }
}
element('ban').addEventListener('click', () => update(true));
element('unban').addEventListener('click', () => update(false));
element('delete').addEventListener('click', async () => {
  if (busy) return;
  const ids = checkedIds();
  if (!ids.length) { status('请先选择设备。'); return; }
  if (!confirm(`删除 ${ids.length} 台设备的关联记录及限制？此操作无法撤销；客户端再次上报时会重新建立记录。`)) return;
  setBusy(true);
  try {
    for (const id of ids) await api(`/api/admin/devices/${encodeURIComponent(id)}`, { method: 'DELETE' });
    await load(currentUid); status('所选记录已删除。');
  } catch (error) { status(`操作未全部完成，请重新查询：${error.message}`); } finally { setBusy(false); }
});
