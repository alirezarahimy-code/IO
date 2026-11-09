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
    await page.addInitScript(() => {
      window.__cspViolations = [];
      document.addEventListener('securitypolicyviolation', e => window.__cspViolations.push(e.violatedDirective));
    });
    await page.goto(address + '/panel');
    const themeSelector = page.locator('.io-theme-picker select');
    const themeNames = ['ocean', 'forest', 'sand', 'night'];
    if (await themeSelector.locator('option').count() !== 4) throw new Error('Expected exactly four central themes');
    const backgrounds = [];
    for (const name of themeNames) {
      await themeSelector.selectOption(name);
      const result = await page.evaluate(() => {
        const root = getComputedStyle(document.documentElement);
        const rgb = value => {
          const hex = value.trim().replace('#', '');
          return [0, 2, 4].map(i => parseInt(hex.slice(i, i + 2), 16) / 255).map(v => v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4);
        };
        const lum = value => { const c = rgb(value); return .2126 * c[0] + .7152 * c[1] + .0722 * c[2]; };
        const contrast = (a, b) => { const x = lum(root.getPropertyValue(a)), y = lum(root.getPropertyValue(b)); return (Math.max(x, y) + .05) / (Math.min(x, y) + .05); };
        return { selected: document.documentElement.dataset.ioTheme, background: root.getPropertyValue('--io-bg').trim(),
          text: contrast('--io-text', '--io-surface'), muted: contrast('--io-muted', '--io-surface'),
          button: contrast('--io-on-primary', '--io-primary'), header: contrast('--io-on-header', '--io-header'),
          border: contrast('--io-border', '--io-surface'), focus: contrast('--io-focus', '--io-surface-alt') };
      });
      if (result.selected !== name || !result.background || Math.min(result.text, result.muted, result.button, result.header) < 4.5 || Math.min(result.border, result.focus) < 3)
        throw new Error('Theme tokens or text contrast are invalid: ' + name);
      backgrounds.push(result.background);
    }
    if (new Set(backgrounds).size !== 4 || await page.evaluate(() => window.IOTheme.set('invalid')) !== false)
      throw new Error('Theme variants or validation failed');
    await page.reload();
    if (await themeSelector.inputValue() !== 'night') throw new Error('Theme did not persist after reload');
    // Exercise a second page that only imports the shared theme assets.
    await context.route('**/theme-fixture', route => route.fulfill({ contentType: 'text/html', body:
      '<!doctype html><html lang="fa" dir="rtl"><head><script src="/assets/theme.js"></script><link rel="stylesheet" href="/assets/theme.css"></head><body><h1>صفحهٔ دوم آزمایشی</h1></body></html>' }));
    const secondPage = await context.newPage();
    await secondPage.goto(address + '/theme-fixture');
    if (await secondPage.locator('.io-theme-picker select').inputValue() !== 'night') throw new Error('Theme did not carry to another page');
    await secondPage.locator('.io-theme-picker select').selectOption('forest');
    await page.waitForFunction(() => document.documentElement.dataset.ioTheme === 'forest');
    if (await themeSelector.inputValue() !== 'forest') throw new Error('Theme did not synchronize between pages');
    await secondPage.close();
    const blockedPage = await context.newPage();
    await blockedPage.addInitScript(() => Object.defineProperty(window, 'localStorage', { get() { throw new Error('disabled'); } }));
    await blockedPage.goto(address + '/theme-fixture');
    if (await blockedPage.locator('.io-theme-picker select').inputValue() !== 'ocean') throw new Error('Unavailable storage did not fall back');
    await blockedPage.locator('.io-theme-picker select').selectOption('sand');
    if (await blockedPage.evaluate(() => document.documentElement.dataset.ioTheme) !== 'sand') throw new Error('Theme failed with unavailable storage');
    await blockedPage.reload();
    if (await blockedPage.locator('.io-theme-picker select').inputValue() !== 'ocean') throw new Error('Unavailable storage was incorrectly persisted');
    await blockedPage.close();
    await page.evaluate(() => localStorage.setItem('io.theme.v1', 'unknown'));
    await page.reload();
    if (await themeSelector.inputValue() !== 'ocean') throw new Error('Unknown saved theme did not fall back safely');
    await page.setViewportSize({ width: 375, height: 812 });
    if (await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)) throw new Error('Theme causes mobile horizontal overflow');
    await page.setViewportSize({ width: 1280, height: 720 });
    if ((await page.evaluate(() => window.__cspViolations)).length) throw new Error('Theme violates Content Security Policy');
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
    const selfId = await page.evaluate(async () => (await (await fetch('/auth/me')).json()).id);
    const selfCompany = await page.evaluate(async id => (await (await fetch('/employees/' + encodeURIComponent(id))).json()).companyId, first);
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
    for (const code of ['organization.manage', 'schedule.manage', 'schedule.read.all', 'attendance.ingest', 'attendance.calculate', 'correction.configure', 'correction.decide', 'correction.decide.all', 'period.preflight', 'period.draft.manage']) {
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
    await page.locator('#correction-policy input[name=companyId]').fill(company);
    await page.locator('#correction-policy input[name=approverUserId]').fill(adminId);
    await page.locator('#correction-policy input[name=name]').fill('Browser synthetic correction policy');
    await submit('correction-policy');
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
    await page.locator('#attendance-event textarea[name=payload]').fill('دادهٔ برخورد ساختگی');
    await clearResult();
    await page.locator('#attendance-event button').click();
    await waitText('(409)');
    await page.locator('#attendance-conflicts input[name=sourceId]').fill(rawSource);
    await clearResult();
    await page.locator('#attendance-conflicts button').click();
    await waitText('browser-event');
    const conflictId = JSON.parse(await page.locator('#result').textContent()).items[0].id;
    await page.locator('#attendance-conflict-note input[name=id]').fill(conflictId);
    await page.locator('#attendance-conflict-note input[name=note]').fill('Browser review note');
    await submit('attendance-conflict-note');
    await page.locator('#attendance-conflict-notes input[name=id]').fill(conflictId);
    await clearResult();
    await page.locator('#attendance-conflict-notes button').click();
    await waitText('Browser review note');
    await clearResult();
    await page.locator('#attendance-list input[name=sourceId]').fill(rawSource);
    await page.locator('#attendance-list button').click();
    await waitText('browser-event');
    await page.locator('#new-employee input[name=companyId]').fill(company);
    await page.locator('#new-employee input[name=personnelNumber]').fill('BROWSER-1');
    await page.locator('#new-employee input[name=displayName]').fill('Browser synthetic employee');
    const employee = await submit('new-employee');
    await page.locator('#attendance-mapping input[name=sourceId]').fill(rawSource);
    await page.locator('#attendance-mapping input[name=personId]').fill('browser-person');
    await page.locator('#attendance-mapping input[name=employeeId]').fill(employee);
    await page.locator('#attendance-mapping input[name=validFrom]').fill('2026-01-01T00:00:00Z');
    await submit('attendance-mapping');
    await page.locator('#attendance-mappings input[name=sourceId]').fill(rawSource);
    await clearResult();
    await page.locator('#attendance-mappings button').click();
    await waitText('browser-person');
    await clearResult();
    await page.locator('#attendance-resolution input[name=sourceId]').fill(rawSource);
    await page.locator('#attendance-resolution button').click();
    await waitText('requires-review');

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
    await page.locator('#calculation-rule input[name=companyId]').fill(company);
    await page.locator('#calculation-rule input[name=name]').fill('Browser paired policy');
    const rule = await submit('calculation-rule');
    await page.locator('#calculation-run input[name=employeeId]').fill(employee);
    await page.locator('#calculation-run input[name=sourceId]').fill(rawSource);
    await page.locator('#calculation-run input[name=ruleVersionId]').fill(rule);
    await page.locator('#calculation-run input[name=workDate]').fill('2027-01-01');
    const run = await submit('calculation-run');
    await page.locator('#calculation-compare input[name=firstId]').fill(run);
    await page.locator('#calculation-compare input[name=secondId]').fill(run);
    await clearResult();
    await page.locator('#calculation-compare button').click();
    await waitText('snapshotDifferent');
    await page.locator('#period-draft input[name=employeeId]').fill(employee);
    await page.locator('#period-draft input[name=start]').fill('2027-01-01');
    await page.locator('#period-draft input[name=endExclusive]').fill('2027-01-02');
    await submit('period-draft');
    await page.locator('#period-drafts input[name=employeeId]').fill(employee);
    await clearResult();
    await page.locator('#period-drafts button').click();
    await waitText('diagnostic-draft-only');
    await page.locator('#period-preflight input[name=employeeId]').fill(employee);
    await page.locator('#period-preflight input[name=start]').fill('2027-01-01');
    await page.locator('#period-preflight input[name=endExclusive]').fill('2027-01-02');
    await clearResult();
    await page.locator('#period-preflight button').click();
    await waitText('approvable');
    await clearResult();
    await page.locator('#calculation-results input[name=employeeId]').fill(employee);
    await page.locator('#calculation-results button').click();
    await waitText('anomaliesJson');

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
    // Exercise the actual self request and distinct approver UI, with explicit grants.
    await page.locator('#role input[name=name]').fill('browser correction self role');
    const selfRole = await submit('role');
    await page.locator('#permission input[name=roleId]').fill(selfRole);
    await page.locator('#permission select[name=code]').selectOption('correction.submit.self');
    await submit('permission');
    await page.locator('#grant input[name=userId]').fill(selfId);
    await page.locator('#grant input[name=roleId]').fill(selfRole);
    await submit('grant');
    await page.locator('#correction-policy input[name=companyId]').fill(selfCompany);
    await page.locator('#correction-policy input[name=approverUserId]').fill(adminId);
    await page.locator('#correction-policy input[name=name]').fill('Browser employee correction policy');
    const employeePolicy = await submit('correction-policy');
    await clearResult();
    await page.locator('#logout').click();
    await waitText('عملیات انجام شد.');
    await login('web-self');
    await clearResult();
    await page.locator('#correction-policies button').click();
    await waitText(employeePolicy);
    await page.locator('#correction-submit input[name=policyId]').fill(employeePolicy);
    await page.locator('#correction-submit input[name=workDate]').fill('2027-03-01');
    await page.locator('#correction-submit input[name=proposedAt]').fill('2027-03-01T08:00:00Z');
    await page.locator('#correction-submit input[name=reason]').fill('Browser missed entry');
    const requestId = await submit('correction-submit');
    await clearResult();
    await page.locator('#correction-own button').click();
    await waitText(requestId);
    await clearResult();
    await page.locator('#logout').click();
    await waitText('عملیات انجام شد.');
    await login(adminName);
    await clearResult();
    await page.locator('#correction-inbox button').click();
    await waitText(requestId);
    await page.locator('#correction-decision input[name=id]').fill(requestId);
    await page.locator('#correction-decision input[name=reason]').fill('Browser verified');
    await submit('correction-decision');
    await clearResult();
    await page.locator('#correction-decisions button').click();
    await waitText(requestId);
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
