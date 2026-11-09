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
form('catalog', async f => show(await fetch('/admin/catalog/' + encodeURIComponent(f.elements.kind.value) + '?offset=' + encodeURIComponent(f.elements.offset.value), { credentials: 'same-origin', cache: 'no-store' })));
form('user', f => { const p = f.elements.password.value; f.elements.password.value = ''; return send('/admin/users', { userName: f.elements.userName.value, password: p, employeeId: f.elements.employeeId.value || null }); });
form('role', f => send('/admin/roles', { name: f.elements.name.value }));
form('permission', f => send('/admin/role-permissions', { roleId: f.elements.roleId.value, code: f.elements.code.value }));
form('grant', f => send('/admin/user-roles', { userId: f.elements.userId.value, roleId: f.elements.roleId.value }));
form('scope', f => send('/admin/user-scopes', { userId: f.elements.userId.value, unitId: f.elements.unitId.value, validFrom: f.elements.validFrom.value, validUntilExclusive: f.elements.validUntilExclusive.value || null }));
form('revoke-role', f => send('/admin/user-roles/revoke', { userId: f.elements.userId.value, roleId: f.elements.roleId.value }));
form('revoke-permission', f => send('/admin/role-permissions/revoke', { roleId: f.elements.roleId.value, code: f.elements.code.value }));
form('revoke-scope', f => send('/admin/user-scopes/' + encodeURIComponent(f.elements.id.value) + '/revoke', null));
form('enable', f => send('/admin/users/' + encodeURIComponent(f.elements.id.value) + '/enable', null));
form('disable', f => send('/admin/users/' + encodeURIComponent(f.elements.id.value) + '/disable', null));
form('org-catalog', async f => show(await fetch('/org/catalog/' + encodeURIComponent(f.elements.kind.value) + '?offset=' + encodeURIComponent(f.elements.offset.value), { credentials: 'same-origin', cache: 'no-store' })));
form('schedule-catalog', async f => show(await fetch('/scheduling/catalog/' + encodeURIComponent(f.elements.kind.value) + '?offset=' + encodeURIComponent(f.elements.offset.value), { credentials: 'same-origin', cache: 'no-store' })));
form('org', f => send('/org/organizations', { name: f.elements.name.value }));
form('company', f => send('/org/companies', { organizationId: f.elements.organizationId.value, name: f.elements.name.value }));
form('site', f => send('/org/sites', { companyId: f.elements.companyId.value, name: f.elements.name.value, timeZoneId: f.elements.timeZoneId.value }));
form('unit', f => send('/org/units', { companyId: f.elements.companyId.value, parentId: f.elements.parentId.value || null, name: f.elements.name.value }));
form('new-employee', f => send('/org/employees', { companyId: f.elements.companyId.value, personnelNumber: f.elements.personnelNumber.value, displayName: f.elements.displayName.value }));
form('employee-rename', f => send('/org/employees/' + encodeURIComponent(f.elements.id.value) + '/rename', { displayName: f.elements.displayName.value, expectedVersion: f.elements.expectedVersion.value }));
form('unit-reparent', f => send('/org/units/' + encodeURIComponent(f.elements.id.value) + '/reparent', { companyId: f.elements.companyId.value, parentId: f.elements.parentId.value || null, expectedVersion: f.elements.expectedVersion.value }));
form('org-assignment', f => send('/org/assignments', { employeeId: f.elements.employeeId.value, unitId: f.elements.unitId.value, validFrom: f.elements.validFrom.value, validUntilExclusive: f.elements.validUntilExclusive.value || null }));
form('employee-site', f => send('/org/site-assignments', { employeeId: f.elements.employeeId.value, siteId: f.elements.siteId.value, validFrom: f.elements.validFrom.value, validUntilExclusive: f.elements.validUntilExclusive.value || null }));
form('site-transfer', f => send('/org/site-transfers', { employeeId: f.elements.employeeId.value, assignmentId: f.elements.assignmentId.value, siteId: f.elements.siteId.value, effective: f.elements.effective.value }));
form('employment', f => send('/org/employment-profiles', { employeeId: f.elements.employeeId.value, statusCode: f.elements.statusCode.value, title: f.elements.title.value || null, validFrom: f.elements.validFrom.value, validUntilExclusive: f.elements.validUntilExclusive.value || null }));
form('employment-change', f => send('/org/employment-changes', { employeeId: f.elements.employeeId.value, profileId: f.elements.profileId.value, statusCode: f.elements.statusCode.value, title: f.elements.title.value || null, effective: f.elements.effective.value }));
form('transfer', f => send('/org/transfers', { employeeId: f.elements.employeeId.value, assignmentId: f.elements.assignmentId.value, unitId: f.elements.unitId.value, effective: f.elements.effective.value }));
form('jalali', async f => show(await fetch('/dates/jalali/' + encodeURIComponent(f.elements.year.value) + '/' + encodeURIComponent(f.elements.month.value) + '/' + encodeURIComponent(f.elements.day.value), { credentials: 'same-origin', cache: 'no-store' })));
form('calendar', f => send('/scheduling/calendars', { companyId: f.elements.companyId.value, name: f.elements.name.value }));
form('calendar-fork', f => send('/scheduling/calendars/' + encodeURIComponent(f.elements.id.value) + '/fork', { name: f.elements.name.value }));
form('calendar-revise', f => send('/scheduling/calendar-days/revise', { calendarId: f.elements.calendarId.value, date: f.elements.date.value, kind: Number(f.elements.kind.value), note: f.elements.note.value || null }));
form('calendar-day', f => send('/scheduling/calendar-days', { calendarId: f.elements.calendarId.value, date: f.elements.date.value, kind: Number(f.elements.kind.value), note: f.elements.note.value || null }));
form('shift', f => send('/scheduling/shifts', { companyId: f.elements.companyId.value, siteId: f.elements.siteId.value, name: f.elements.name.value, startsAt: f.elements.startsAt.value, endsAt: f.elements.endsAt.value, endDayOffset: Number(f.elements.endDayOffset.value) }));
form('schedule-assignment', f => send('/scheduling/assignments', { employeeId: f.elements.employeeId.value, calendarId: f.elements.calendarId.value, shiftId: f.elements.shiftId.value, validFrom: f.elements.validFrom.value, validUntilExclusive: f.elements.validUntilExclusive.value || null }));
form('schedule-replacement', f => send('/scheduling/replacements', { employeeId: f.elements.employeeId.value, assignmentId: f.elements.assignmentId.value, calendarId: f.elements.calendarId.value, shiftId: f.elements.shiftId.value, effective: f.elements.effective.value }));
form('schedule-view', async f => show(await fetch('/schedules/' + encodeURIComponent(f.elements.employeeId.value) + '/' + encodeURIComponent(f.elements.workDate.value), { credentials: 'same-origin', cache: 'no-store' })));
form('attendance-source', f => send('/attendance/sources', { name: f.elements.name.value, siteId: f.elements.siteId.value || null }));
form('attendance-event', f => {
  const bytes = new TextEncoder().encode(f.elements.payload.value);
  let binary = ''; for (const byte of bytes) binary += String.fromCharCode(byte);
  return send('/attendance/simulated-events', { sourceId: f.elements.sourceId.value, externalId: f.elements.externalId.value || null,
    personId: f.elements.personId.value || null, occurredAt: f.elements.occurredAt.value || null, sourceZone: f.elements.sourceZone.value || null,
    eventType: f.elements.eventType.value || null, payload: btoa(binary) });
});
form('attendance-list', async f => show(await fetch('/attendance/raw/' + encodeURIComponent(f.elements.sourceId.value) + '?offset=' + encodeURIComponent(f.elements.offset.value), { credentials: 'same-origin', cache: 'no-store' })));
form('attendance-mapping', f => send('/attendance/mappings', { sourceId: f.elements.sourceId.value, sourcePersonId: f.elements.personId.value, employeeId: f.elements.employeeId.value, validFrom: f.elements.validFrom.value, validUntilExclusive: f.elements.validUntilExclusive.value || null }));
form('attendance-resolution', async f => show(await fetch('/attendance/resolution/' + encodeURIComponent(f.elements.sourceId.value) + '?offset=' + encodeURIComponent(f.elements.offset.value), { credentials: 'same-origin', cache: 'no-store' })));
form('attendance-conflicts', async f => show(await fetch('/attendance/conflicts/' + encodeURIComponent(f.elements.sourceId.value) + '?offset=' + encodeURIComponent(f.elements.offset.value), { credentials: 'same-origin', cache: 'no-store' })));
form('attendance-conflict', async f => show(await fetch('/attendance/conflict/' + encodeURIComponent(f.elements.id.value), { credentials: 'same-origin', cache: 'no-store' })));
form('calculation-rule', f => send('/calculations/rules', { companyId: f.elements.companyId.value, name: f.elements.name.value, beforeMinutes: Number(f.elements.beforeMinutes.value), afterMinutes: Number(f.elements.afterMinutes.value) }));
form('calculation-run', f => send('/calculations/runs', { employeeId: f.elements.employeeId.value, sourceId: f.elements.sourceId.value, ruleVersionId: f.elements.ruleVersionId.value, workDate: f.elements.workDate.value }));
form('calculation-compare', async f => show(await fetch('/calculations/compare/' + encodeURIComponent(f.elements.firstId.value) + '/' + encodeURIComponent(f.elements.secondId.value), { credentials: 'same-origin', cache: 'no-store' })));
form('calculation-results', async f => show(await fetch('/calculations/runs/' + encodeURIComponent(f.elements.employeeId.value) + '?offset=' + encodeURIComponent(f.elements.offset.value), { credentials: 'same-origin', cache: 'no-store' })));
form('correction-policy', f => send('/corrections/policies', { companyId: f.elements.companyId.value, approverUserId: f.elements.approverUserId.value, name: f.elements.name.value }));
for (const [id, path] of [['correction-policies', 'policies'], ['correction-own', 'own'], ['correction-inbox', 'inbox']])
  form(id, async f => show(await fetch('/corrections/' + path + '?offset=' + encodeURIComponent(f.elements.offset.value), { credentials: 'same-origin', cache: 'no-store' })));
form('correction-submit', f => send('/corrections/requests', { policyId: f.elements.policyId.value, workDate: f.elements.workDate.value, proposedAt: f.elements.proposedAt.value, punchKind: Number(f.elements.punchKind.value), reason: f.elements.reason.value }));
form('correction-decision', f => send('/corrections/requests/' + encodeURIComponent(f.elements.id.value) + '/decision', { approve: f.elements.decision.value === 'approve', reason: f.elements.reason.value || null }));
form('correction-cancel', f => send('/corrections/requests/' + encodeURIComponent(f.elements.id.value) + '/cancel', { reason: f.elements.reason.value }));
form('period-draft', f => send('/periods/drafts', { employeeId: f.elements.employeeId.value, start: f.elements.start.value, endExclusive: f.elements.endExclusive.value }));
form('period-drafts', async f => show(await fetch('/periods/drafts/' + encodeURIComponent(f.elements.employeeId.value) + '?offset=' + encodeURIComponent(f.elements.offset.value), { credentials: 'same-origin', cache: 'no-store' })));
form('period-preflight', async f => show(await fetch('/periods/preflight/' + encodeURIComponent(f.elements.employeeId.value) + '?start=' + encodeURIComponent(f.elements.start.value) + '&endExclusive=' + encodeURIComponent(f.elements.endExclusive.value), { credentials: 'same-origin', cache: 'no-store' })));
document.getElementById('logout').addEventListener('click', async () => { try { await send('/auth/logout', null); } catch { output.textContent = 'خروج ناموفق بود.'; } });
