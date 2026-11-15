// Screenshots of the web panel on a running server (CI):  node panel-screenshots.mjs <url> <admin> <password> <out-folder>
import { chromium } from 'playwright';
const [url, admin, password, out] = process.argv.slice(2);
const browser = await chromium.launch();
for (const scheme of ['light', 'dark']) {
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, colorScheme: scheme, deviceScaleFactor: 1 });
  page.on('pageerror', (e) => { console.error('Page error:', e.message); process.exitCode = 1; });
  await page.goto(url);
  await page.waitForSelector('form');
  if (scheme === 'light') await page.screenshot({ path: `${out}/panel-00-sign-in.png` });
  await page.fill('#u', admin);
  await page.fill('#p', password);
  await page.click('button.primary');
  await page.waitForSelector('.shell');
  let n = 1;
  for (const id of ['overview', 'users', 'invites', 'settings', 'logs']) {
    await page.click(`[data-page="${id}"]`);
    await page.waitForTimeout(900);
    await page.screenshot({ path: `${out}/panel-${String(n++).padStart(2, '0')}-${id}-${scheme}.png`, fullPage: true });
    if (id === 'users') {
      await page.click('[data-user]');
      await page.waitForSelector('.drawer');
      await page.waitForTimeout(400);
      await page.screenshot({ path: `${out}/panel-${String(n++).padStart(2, '0')}-user-${scheme}.png` });
      await page.keyboard.press('Escape');
    }
  }
  await page.click('[data-page="overview"]');
  await page.waitForTimeout(500);
  await page.click('[data-act="stop"]');
  await page.waitForSelector('.modal');
  await page.screenshot({ path: `${out}/panel-${String(n++).padStart(2, '0')}-confirm-stop-${scheme}.png` });
  await page.click('.modal [data-yes]');
  await page.waitForTimeout(800);
  await page.screenshot({ path: `${out}/panel-${String(n++).padStart(2, '0')}-stopped-${scheme}.png` });
  await page.click('[data-act="start"]');
  await page.waitForTimeout(500);
  await page.close();
}
await browser.close();
