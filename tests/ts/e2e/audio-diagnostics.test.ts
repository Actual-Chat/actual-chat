/**
 * E2E test: Audio Diagnostics modal opens from Developer tools and renders its tabs.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/audio-diagnostics.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, beforeAll, afterAll } from 'vitest';
import type { Page } from 'playwright';
import {
    BASE_URL, connectBrowser, dismissCookieConsent, ensureSignedIn,
    screenshot, waitForAppReady, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e', `audio-diagnostics-${name}`);

describe('Audio Diagnostics modal opens from Developer tools', () => {
    let conn: BrowserConnection;
    let page: Page;

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();
        await page.goto(`${BASE_URL}/fusion/renderMode/w`, { waitUntil: 'domcontentloaded' });
        await ensureSignedIn(page);
    }, 120_000);

    afterAll(async () => {
        await page.close();
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    });

    it('enables audio diagnostics, opens the modal, and shows the session tab', async () => {
        await page.goto(`${BASE_URL}/settings`, { waitUntil: 'domcontentloaded' });
        await waitForAppReady(page);
        await dismissCookieConsent(page);

        const devToolsTab = page.locator('.settings-tab-item:has-text("Developer tools")').first();
        await devToolsTab.waitFor({ state: 'visible', timeout: 15_000 });
        await devToolsTab.click({ force: true });

        // Enable the flag; the "Open audio diagnostics" row only appears once it's on.
        // Gate on that row (the real outcome) rather than the toggle attribute, which
        // re-renders a beat after the click.
        const enable = page.locator(
            '.settings-tab-content .tile-item:has-text("Enable audio diagnostics")',
        ).first();
        await enable.waitFor({ state: 'visible', timeout: 10_000 });
        const open = page.locator(
            '.settings-tab-content .tile-item:has-text("Open audio diagnostics")',
        ).first();
        for (let i = 0; i < 6; i++) {
            if (await open.isVisible().catch(() => false))
                break;

            await enable.click({ force: true });
            await page.waitForTimeout(1000);
        }
        await open.waitFor({ state: 'visible', timeout: 10_000 });
        await open.click({ force: true });

        const modal = page.locator('.audio-diagnostics-modal').first();
        await modal.waitFor({ state: 'visible', timeout: 10_000 });

        // The Session tab and the native audio-session section render on the web build
        // (Web Audio context is web-only; the native session shows "not managed").
        await modal.locator('.diag-tab:has-text("Session")').first()
            .waitFor({ state: 'visible', timeout: 5_000 });
        await modal.locator('.diag-section-header:has-text("Native audio session")').first()
            .waitFor({ state: 'visible', timeout: 5_000 });

        await page.screenshot({ path: shot('opened') });
    }, 60_000);
});
