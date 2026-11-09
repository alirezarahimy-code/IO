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
