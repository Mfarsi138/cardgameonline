// Run with Playwright installed locally or available through NODE_PATH.
const { chromium, expect } = require('playwright/test');
const fs = require('node:fs/promises');
const path = require('node:path');

(async () => {
  const browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL || 'msedge', headless: true });
  const output = path.resolve(__dirname, '../../TestResults/club-layout');
  await fs.mkdir(output, { recursive: true });
  const errors = [];
  try {
    for (const [width, height] of [[360, 800], [390, 844], [768, 1024], [1440, 900], [844, 390]]) {
      const page = await browser.newPage({ viewport: { width, height }, reducedMotion: 'reduce' });
      page.on('pageerror', error => errors.push(error.message));
      await page.goto(process.env.BASE_URL || 'http://127.0.0.1:5188');
      await expect(page.locator('.club-shell')).toHaveAttribute('data-ready', 'true');
      for (const section of ['Play', 'Collection', 'Friends', 'Journey', 'Profile']) {
        await page.locator('.club-nav').getByRole('button', { name: section, exact: true }).click();
        await expect(page.locator('.club-nav button[aria-current="page"]')).toHaveText(new RegExp(section));
        await page.evaluate(() => window.scrollTo(0, 0));
        const nav = page.locator('.club-nav');
        const before = await nav.boundingBox();
        await page.mouse.move(width / 2, height / 2);
        await page.mouse.wheel(0, 10000);
        await expect.poll(() => page.evaluate(() => {
          const root = document.scrollingElement;
          return root.scrollHeight <= innerHeight + 1 || root.scrollTop > 0;
        })).toBe(true);
        await page.evaluate(() => window.scrollTo(0, document.scrollingElement.scrollHeight));
        const after = await nav.boundingBox();
        if (Math.abs(before.y - after.y) > 1 || Math.abs(after.y + after.height - height) > 1) {
          throw new Error(`${width}/${section}: navigation moved or is not bottom-aligned`);
        }
        const layout = await page.evaluate(() => ({
          overflow: document.documentElement.scrollWidth > innerWidth,
          contentBottom: document.querySelector('.club-main').getBoundingClientRect().bottom,
          navTop: document.querySelector('.club-nav').getBoundingClientRect().top,
          scrollable: document.scrollingElement.scrollHeight > innerHeight + 1
        }));
        if (layout.overflow || (layout.scrollable && layout.contentBottom > layout.navTop)) {
          throw new Error(`${width}/${section}: content clipped by navigation or horizontal overflow`);
        }
        for (const button of await nav.getByRole('button').all()) {
          const box = await button.boundingBox();
          if (box.width < 44 || box.height < 44) throw new Error('Navigation touch target below 44px');
        }
        await page.screenshot({ path: path.join(output, `${width}-${height}-${section}.png`), fullPage: true });
      }
      await page.close();
    }
    if (errors.length) throw new Error(errors.join('\n'));
    console.log('PASS: 25 section/viewport combinations; wheel scroll, fixed compact navigation, reachable content, touch targets, no overflow or page errors.');
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
