'use strict';
const { chromium } = require('playwright-core');
const fs = require('fs');
const assert = require('assert/strict');
const path = require('path');
const base = process.argv[2];
if (!base || !/^http:\/\/127\.0\.0\.1:\d+\/$/.test(base)) throw Error('Expected isolated local static server.');
const pages = JSON.parse(fs.readFileSync(path.join(__dirname, 'pages.json'), 'utf8'));
(async () => {
  assert.equal(pages.length, 34);
  assert.equal(new Set(pages.map(p => p.id)).size, pages.length);
  assert(pages.every(p => /^[a-z0-9-]+$/.test(p.id) && p.rows.every(row => row.length === p.columns.length)));
  assert(!pages.some(p => /حقوق و دستمزد|گیت|دربازکن/.test(p.title + p.description)));
  const browser = await chromium.launch({ executablePath: process.env.IO_CHROME_BINARY, headless: true, args: ['--no-sandbox'] });
  try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    for (const item of pages) {
      await page.goto(base + 'preview/pages-first/#' + item.id);
      await page.locator('#page-content h1').waitFor();
      assert.equal(await page.locator('#page-content h1').textContent(), item.title);
      assert.equal(await page.locator('#navigation a[aria-current="page"]').getAttribute('href'), '#' + item.id);
      const schema = await page.evaluate(id => Boolean(window.IOSchemaPages[id]), item.id);
      if (schema) {
        assert((await page.locator('#page-content tbody tr').count()) > 0);
        assert.equal(await page.locator('#page-content .actions button:not(:disabled)').count(), 0);
      } else {
        assert.equal(await page.locator('#page-content tbody tr').count(), item.rows.length);
        assert.equal(await page.locator('#page-content input:not(:disabled), #page-content button:not(:disabled)').count(), 0);
      }
      assert.equal(await page.locator('.demo-banner').isVisible(), true);
    }
    // Verify every rendered schema field against the current create-input record.
    const contractSource = fs.readFileSync(path.join(__dirname, '../../src/IO.Web/Program.cs'), 'utf8');
    const specs = await page.evaluate(() => window.IOSchemaPages);
    for (const [id, tabs] of Object.entries(specs)) {
      await page.goto(base + 'preview/pages-first/#' + id);
      await page.waitForFunction(expected => document.querySelector('#page-content h1')?.textContent === expected, pages.find(p => p.id === id).title);
      for (const tab of tabs) {
        await page.locator(`[data-schema="${tab.key}"]`).click();
        const declaration = contractSource.match(new RegExp('public sealed record ' + tab.contract + '\\(([^)]*)\\);'));
        assert(declaration, 'Missing contract ' + tab.contract);
        const expected = declaration[1].split(',').map(part => part.trim().split(/\s+/).pop()).map(name => name[0].toLowerCase() + name.slice(1)).sort();
        assert.deepEqual(tab.fields.map(field => field[0]).sort(), expected, 'Input fields drifted: ' + tab.contract);
        for (const field of tab.fields) assert.equal(await page.locator('[name="' + field[0] + '"]').count(), 1);
      }
    }
    await page.goto(base + 'preview/pages-first/#org');
    await page.waitForFunction(() => document.querySelector('#page-content h1')?.textContent === 'ساختار سازمانی');
    await page.locator('[data-schema="sites"]').click();
    assert.equal(await page.locator('[name="timeZoneId"]').getAttribute('maxlength'), '100');
    assert.equal(await page.locator('[name="companyId"]').count(), 1);
    await page.locator('#schema-search').fill('چیزی که نیست');
    assert(await page.locator('#schema-empty').isVisible());
    await page.goto(base + 'preview/pages-first/#employees');
    await page.waitForFunction(() => document.querySelector('#page-content h1')?.textContent === 'کارکنان و پرونده‌ها');
    assert.equal(await page.locator('[name="personnelNumber"]').getAttribute('maxlength'), '80');
    assert.equal(await page.locator('[name="displayName"]').getAttribute('maxlength'), '200');
    await page.locator('[name="displayName"]').fill('نام نمونه');
    assert(await page.locator('.actions button').isDisabled());
    for (const name of ['ocean', 'forest', 'sand', 'night']) {
      await page.locator('.io-theme-picker select').selectOption(name);
      assert.equal(await page.locator('html').getAttribute('data-io-theme'), name);
    }
    await page.reload();
    assert.equal(await page.locator('html').getAttribute('data-io-theme'), 'night');
    await page.locator('.io-theme-picker select').selectOption('ocean');
    await page.locator('#page-search').fill('کارکنان');
    assert((await page.locator('#navigation a').count()) > 0);
    await page.locator('#page-search').fill('متنی بدون نتیجه');
    assert(await page.locator('#no-pages').isVisible());
    await page.locator('#page-search').fill('');
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto(base + 'preview/pages-first/#employees');
    await page.locator('#page-content h1').waitFor();
    await page.locator('#menu-toggle').click();
    assert.equal(await page.locator('#menu-toggle').getAttribute('aria-expanded'), 'true');
    await page.locator('#navigation a[href="#inbox"]').click();
    await page.waitForFunction(() => document.querySelector('#page-content h1')?.textContent === 'کارتابل بررسی درخواست');
    assert.equal(await page.locator('#page-content h1').textContent(), 'کارتابل بررسی درخواست');
    assert.equal(await page.locator('#menu-toggle').getAttribute('aria-expanded'), 'false');
    assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
    await page.goto(base + 'preview/pages-first/#%E0%A4%A');
    await page.locator('#page-content h1').waitFor();
    assert.equal(await page.locator('#page-content h1').textContent(), pages[0].title);
    assert.deepEqual(errors, []);
    console.log('PASS 34 preview routes, schema fields, theme persistence, search and mobile navigation');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
