'use strict';
const output = document.getElementById('result');
async function token() {
  const response = await fetch('/auth/csrf', { credentials: 'same-origin', cache: 'no-store' });
  if (!response.ok) throw new Error('دریافت شناسهٔ امنیتی ناموفق بود');
  return (await response.json()).token;
}
async function send(path, body) {
  const csrf = await token();
  const response = await fetch(path, { method: 'POST', credentials: 'same-origin', cache: 'no-store',
    headers: { 'X-CSRF-TOKEN': csrf, ...(body === null ? {} : { 'Content-Type': 'application/json' }) },
    body: body === null ? null : JSON.stringify(body) });
  await show(response);
}
async function show(response) {
  const text = await response.text();
  output.textContent = response.ok ? (text || 'عملیات انجام شد.') : `درخواست رد شد (${response.status}).`;
}
function form(id, handler) {
  document.getElementById(id).addEventListener('submit', async event => {
    event.preventDefault();
    try { await handler(event.currentTarget); } catch { output.textContent = 'درخواست انجام نشد.'; }
  });
}
form('login', f => { const p = f.elements.password.value; f.elements.password.value = ''; return send('/auth/login', { userName: f.elements.userName.value, password: p }); });
form('password-change', f => { const old = f.elements.currentPassword.value; const next = f.elements.newPassword.value; f.elements.currentPassword.value = ''; f.elements.newPassword.value = ''; return send('/auth/change-password', { currentPassword: old, newPassword: next }); });
form('employee', async f => show(await fetch('/employees/' + encodeURIComponent(f.elements.id.value), { credentials: 'same-origin', cache: 'no-store' })));
form('user', f => { const p = f.elements.password.value; f.elements.password.value = ''; return send('/admin/users', { userName: f.elements.userName.value, password: p, employeeId: f.elements.employeeId.value || null }); });
form('role', f => send('/admin/roles', { name: f.elements.name.value }));
form('permission', f => send('/admin/role-permissions', { roleId: f.elements.roleId.value, code: f.elements.code.value }));
form('grant', f => send('/admin/user-roles', { userId: f.elements.userId.value, roleId: f.elements.roleId.value }));
form('scope', f => send('/admin/user-scopes', { userId: f.elements.userId.value, unitId: f.elements.unitId.value, validFrom: f.elements.validFrom.value, validUntilExclusive: f.elements.validUntilExclusive.value || null }));
form('disable', f => send('/admin/users/' + encodeURIComponent(f.elements.id.value) + '/disable', null));
document.getElementById('logout').addEventListener('click', async () => { try { await send('/auth/logout', null); } catch { output.textContent = 'خروج ناموفق بود.'; } });
