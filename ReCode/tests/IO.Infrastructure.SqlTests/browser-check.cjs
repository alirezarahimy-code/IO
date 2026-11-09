'use strict';
// Only synthetic credentials and an ephemeral loopback server are used here.
const { chromium } = require('playwright-core');
const [address, first, second, adminName] = process.argv.slice(2);
if (!address || !first || !second || !adminName || !address.startsWith('https://127.0.0.1:'))
  throw new Error('Expected the disposable HTTPS loopback server and synthetic IDs.');
async function main() {
  const browser = await chromium.launch({ executablePath: process.env.IO_CHROME_BINARY, headless: true, args: ['--no-sandbox'] });
  try {
    const context = await browser.newContext({ ignoreHTTPSErrors: true });
    const page = await context.newPage();
    await page.goto(address + '/panel');
    async function waitText(text) {
      await page.waitForFunction(expected => document.getElementById('result').textContent.includes(expected), text, { timeout: 10000 });
    }
    async function clearResult() { await page.locator('#result').evaluate(el => { el.textContent = ''; }); }
    async function login(name) {
      await clearResult();
      await page.locator('#login input[name=userName]').fill(name);
      await page.locator('#login input[name=password]').fill('Synthetic!Password-2026');
      await page.locator('#login button').click();
      await waitText('عملیات انجام شد.');
    }
    await login('web-self');
    await clearResult();
    await page.locator('#employee input[name=id]').fill(first);
    await page.locator('#employee button').click();
    await waitText('WEB-1');
    await clearResult();
    await page.locator('#employee input[name=id]').fill(second);
    await page.locator('#employee button').click();
    await waitText('(404)');
    await clearResult();
    await page.locator('#role input[name=name]').fill('unauthorized-browser-role');
    await page.locator('#role button').click();
    await waitText('(403)');
    await clearResult();
    await page.locator('#logout').click();
    await waitText('عملیات انجام شد.');
    await login(adminName);
    await clearResult();
    await page.locator('#role input[name=name]').fill('synthetic-browser-role');
    await page.locator('#role button').click();
    await page.waitForFunction(() => document.getElementById('result').textContent.includes('result'), null, { timeout: 10000 });
    const role = JSON.parse(await page.locator('#result').textContent()).result;
    const adminId = await page.evaluate(async () => (await (await fetch('/auth/me')).json()).id);
    async function submit(formId) {
      await clearResult();
      await page.locator('#' + formId + ' button').click();
      await waitText('result');
      return JSON.parse(await page.locator('#result').textContent()).result;
    }
    for (const code of ['organization.manage', 'schedule.manage', 'schedule.read.all', 'attendance.ingest']) {
      await page.locator('#permission input[name=roleId]').fill(role);
      await page.locator('#permission select[name=code]').selectOption(code);
      await submit('permission');
    }
    await page.locator('#grant input[name=userId]').fill(adminId);
    await page.locator('#grant input[name=roleId]').fill(role);
    await submit('grant');
    await page.locator('#org input[name=name]').fill('Browser synthetic org');
    const org = await submit('org');
    await page.locator('#company input[name=organizationId]').fill(org);
    await page.locator('#company input[name=name]').fill('Browser synthetic company');
    const company = await submit('company');
    await page.locator('#site input[name=companyId]').fill(company);
    await page.locator('#site input[name=name]').fill('Browser synthetic site');
    await page.locator('#site input[name=timeZoneId]').fill('Asia/Tehran');
    const site = await submit('site');
    await page.locator('#attendance-source input[name=name]').fill('Browser synthetic source');
    await page.locator('#attendance-source input[name=siteId]').fill(site);
    const rawSource = await submit('attendance-source');
    await page.locator('#attendance-event input[name=sourceId]').fill(rawSource);
    await page.locator('#attendance-event input[name=externalId]').fill('browser-event');
    await page.locator('#attendance-event input[name=occurredAt]').fill('2026-01-01T08:00:00Z');
    await page.locator('#attendance-event textarea[name=payload]').fill('دادهٔ ساختگی');
    await submit('attendance-event');
    await clearResult();
    await page.locator('#attendance-list input[name=sourceId]').fill(rawSource);
    await page.locator('#attendance-list button').click();
    await waitText('browser-event');
    await page.locator('#new-employee input[name=companyId]').fill(company);
    await page.locator('#new-employee input[name=personnelNumber]').fill('BROWSER-1');
    await page.locator('#new-employee input[name=displayName]').fill('Browser synthetic employee');
    const employee = await submit('new-employee');
    await page.locator('#employee-site input[name=employeeId]').fill(employee);
    await page.locator('#employee-site input[name=siteId]').fill(site);
    await page.locator('#employee-site input[name=validFrom]').fill('2027-01-01');
    const siteAssignment = await submit('employee-site');
    await page.locator('#employment input[name=employeeId]').fill(employee);
    await page.locator('#employment input[name=statusCode]').fill('browser-status');
    await page.locator('#employment input[name=validFrom]').fill('2027-01-01');
    const profile = await submit('employment');
    await page.locator('#site-transfer input[name=employeeId]').fill(employee);
    await page.locator('#site-transfer input[name=assignmentId]').fill(siteAssignment);
    await page.locator('#site-transfer input[name=siteId]').fill(site);
    await page.locator('#site-transfer input[name=effective]').fill('2027-02-01');
    await submit('site-transfer');
    await page.locator('#employment-change input[name=employeeId]').fill(employee);
    await page.locator('#employment-change input[name=profileId]').fill(profile);
    await page.locator('#employment-change input[name=statusCode]').fill('browser-next-status');
    await page.locator('#employment-change input[name=effective]').fill('2027-02-01');
    await submit('employment-change');
    await page.locator('#calendar input[name=companyId]').fill(company);
    await page.locator('#calendar input[name=name]').fill('Browser calendar');
    const calendar = await submit('calendar');
    await page.locator('#calendar-day input[name=calendarId]').fill(calendar);
    await page.locator('#calendar-day input[name=date]').fill('2027-01-01');
    await page.locator('#calendar-day select[name=kind]').selectOption('1');
    await submit('calendar-day');
    await page.locator('#shift input[name=companyId]').fill(company);
    await page.locator('#shift input[name=siteId]').fill(site);
    await page.locator('#shift input[name=name]').fill('Browser night shift');
    await page.locator('#shift input[name=startsAt]').fill('22:00');
    await page.locator('#shift input[name=endsAt]').fill('06:00');
    await page.locator('#shift select[name=endDayOffset]').selectOption('1');
    const shift = await submit('shift');
    await page.locator('#schedule-assignment input[name=employeeId]').fill(employee);
    await page.locator('#schedule-assignment input[name=calendarId]').fill(calendar);
    await page.locator('#schedule-assignment input[name=shiftId]').fill(shift);
    await page.locator('#schedule-assignment input[name=validFrom]').fill('2027-01-01');
    await submit('schedule-assignment');
    await clearResult();
    await page.locator('#schedule-view input[name=employeeId]').fill(employee);
    await page.locator('#schedule-view input[name=workDate]').fill('2027-01-01');
    await page.locator('#schedule-view button').click();
    await waitText('Browser night shift');
    await clearResult();
    await page.locator('#jalali input[name=year]').fill('1403');
    await page.locator('#jalali input[name=month]').fill('1');
    await page.locator('#jalali input[name=day]').fill('1');
    await page.locator('#jalali button').click();
    await waitText('2024-03-20');
    await clearResult();
    await page.locator('#logout').click();
    await waitText('عملیات انجام شد.');
    await context.close();
    console.log('Browser form scenarios passed.');
  } finally {
    await browser.close();
  }
}
main().catch(e => { console.error(e.message); process.exitCode = 1; });
