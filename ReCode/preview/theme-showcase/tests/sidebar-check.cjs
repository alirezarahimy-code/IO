'use strict';
// Serve the showcase separately and run with NODE_PATH containing playwright-core.
const { chromium } = require('playwright-core');
const address = process.env.IO_PREVIEW_URL;
if (!address || !process.env.IO_CHROME_BINARY) throw new Error('IO_PREVIEW_URL and IO_CHROME_BINARY are required');
(async () => {
  const browser = await chromium.launch({ executablePath: process.env.IO_CHROME_BINARY, args: ['--no-sandbox'], headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    page.on('pageerror', error => { throw error; });
    await page.goto(address);
    for (const theme of ['ocean', 'forest', 'sand', 'night']) {
      await page.locator(`[data-choice="${theme}"]`).click();
      const label = page.locator('.nav-item[aria-label="ترددهای من"] span');
      if (!await label.isVisible()) throw new Error('Expanded sidebar hidden: ' + theme);
      const expandedWidth = await page.locator('.sidebar').evaluate(el => el.getBoundingClientRect().width);
      await page.locator('.topbar-toggle').click();
      const collapsedWidth = await page.locator('.sidebar').evaluate(el => el.getBoundingClientRect().width);
      if (await label.isVisible() || await page.locator('.topbar-toggle').getAttribute('aria-expanded') !== 'false' ||
          expandedWidth < 200 || collapsedWidth > 90 || collapsedWidth >= expandedWidth) throw new Error('Collapse failed: ' + theme);
      await page.locator('.topbar-toggle').click();
      if (!await label.isVisible() || await page.locator('.topbar-toggle').getAttribute('aria-expanded') !== 'true') throw new Error('Expand failed: ' + theme);
    }
    await page.locator('.topbar-toggle').click();
    await page.locator('[data-choice="ocean"]').click();
    if (await page.locator('.showcase').getAttribute('data-sidebar') !== 'closed' ||
        await page.locator('.nav-item[aria-label="ترددهای من"] span').isVisible()) throw new Error('Changing theme must not reopen a collapsed menu');
    await page.setViewportSize({ width: 375, height: 812 });
    for (const theme of ['ocean', 'forest', 'sand', 'night']) {
      await page.locator(`[data-choice="${theme}"]`).click();
      if (await page.locator('.sidebar').isVisible()) throw new Error('Mobile sidebar should start closed: ' + theme);
      await page.locator('.topbar-toggle').click();
      if (!await page.locator('.sidebar').isVisible() || !await page.locator('.nav-item[aria-label="ترددهای من"] span').isVisible() ||
          await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)) throw new Error('Mobile sidebar failed: ' + theme);
      await page.keyboard.press('Escape');
      if (await page.locator('.sidebar').isVisible()) throw new Error('Mobile Escape failed: ' + theme);
      await page.locator('.topbar-toggle').click();
      await page.locator('.sidebar-close').click();
      if (await page.locator('.sidebar').isVisible()) throw new Error('Mobile close button failed: ' + theme);
    }
    console.log('Four themes: desktop and mobile sidebar checks passed.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
