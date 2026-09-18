// Requires Playwright on Node's module path and a running local web app.
const { chromium, expect } = require('playwright/test');
const fs = require('node:fs/promises');
const path = require('node:path');

(async () => {
  const browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL || 'msedge', headless: true });
  const errors = [];
  const output = path.resolve(__dirname, '../../TestResults/browser');
  await fs.mkdir(output, { recursive: true });
  try {
    const host = await browser.newPage({ viewport: { width: 390, height: 844 } });
    const guest = await browser.newPage({ viewport: { width: 390, height: 844 } });
    for (const page of [host, guest]) page.on('pageerror', error => errors.push(error.message));
    await host.goto(process.env.BASE_URL || 'http://127.0.0.1:5188');
    await expect(host.locator('.club-shell')).toHaveAttribute('data-ready', 'true');
    await host.screenshot({ path: path.join(output, 'home-mobile.png'), fullPage: true });
    await host.getByRole('button', { name: 'Create New Room', exact: true }).click();
    await expect(host.locator('.room-lobby-card')).toBeVisible();
    const invite = await host.locator('.share-input-row input').inputValue();
    await guest.goto(invite);
    await guest.getByRole('button', { name: 'Join This Room', exact: true }).click();
    await expect(guest.locator('.room-lobby-card')).toBeVisible();
    await expect(host.locator('.lobby-player-row')).toHaveCount(2);
    await expect(guest.locator('.lobby-player-row')).toHaveCount(2);
    await expect(guest.getByRole('button', { name: 'Start Match', exact: true })).toHaveCount(0);
    await guest.screenshot({ path: path.join(output, 'invitation-mobile.png'), fullPage: true });
    await host.getByRole('button', { name: 'Start Match', exact: true }).click();
    for (const page of [host, guest]) await expect(page.locator('.game-shell')).toBeVisible();
    const timer = host.locator('.turn-timer-text');
    await expect(timer).toBeVisible({ timeout: 15000 });
    const initial = parseInt(await timer.innerText(), 10);
    await expect.poll(async () => parseInt(await timer.innerText(), 10), { timeout: 5000 }).toBeLessThan(initial);
    await host.locator('.reaction-toggle').click();
    await expect(host.locator('.reaction-picker')).toBeVisible();
    await host.locator('.reaction-grid button').first().click();
    await expect(host.locator('.reaction-picker')).toHaveCount(0);
    await expect(host.locator('.human-seat > .seat-reaction')).toBeVisible();
    await expect(guest.locator('.orbit-seat:not(.human-seat) > .seat-reaction')).toBeVisible();
    await host.screenshot({ path: path.join(output, 'reaction-sender.png') });
    await guest.screenshot({ path: path.join(output, 'reaction-receiver.png') });
    await expect(host.locator('.seat-reaction')).toHaveCount(0, { timeout: 6000 });
    await host.locator('.reaction-toggle').click();
    await host.getByRole('button', { name: 'Mute reactions', exact: true }).click();
    await host.locator('.reaction-grid button').first().click();
    await expect(host.locator('.seat-reaction')).toHaveCount(0);
    await expect(guest.locator('.seat-reaction')).toHaveCount(1);
    for (const page of [host, guest]) {
      if (await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)) throw new Error('Mobile horizontal overflow');
    }
    await host.screenshot({ path: path.join(output, 'online-table-mobile.png'), fullPage: true });
    if (errors.length) throw new Error(errors.join('\n'));
    console.log('PASS: invitation, countdown, seat reactions, expiry, mute, mobile overflow and page errors');
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
