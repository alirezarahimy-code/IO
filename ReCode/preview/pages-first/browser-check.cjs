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
      assert.equal(await page.locator('#page-content tbody tr').count(), item.rows.length);
      assert.equal(await page.locator('#page-content input:not(:disabled), #page-content button:not(:disabled)').count(), 0);
      assert.equal(await page.locator('.demo-banner').isVisible(), true);
    }
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
    console.log('PASS 34 page preview routes, disabled controls, search and mobile navigation');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
