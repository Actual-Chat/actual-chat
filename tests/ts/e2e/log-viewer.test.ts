/**
 * E2E test: Settings > Log Viewer tab is reachable on narrow screens.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/log-viewer.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, beforeAll, afterAll } from 'vitest';
import type { Page } from 'playwright';
import {
    BASE_URL, connectBrowser, dismissCookieConsent, ensureSignedIn,
    screenshot, waitForAppReady, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e', `log-viewer-${name}`);

describe('Log Viewer tab is reachable on narrow screens', () => {
    let conn: BrowserConnection;
    let page: Page;

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();
        await page.setViewportSize({ width: 393, height: 852 });
        // Log Viewer tab is hidden in SSB (LogUI.CanCapture is false there).
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

    it('enables Log Viewer via Developer tools and opens the tab', async () => {
        await page.goto(`${BASE_URL}/settings`, { waitUntil: 'domcontentloaded' });
        await waitForAppReady(page);
        await dismissCookieConsent(page);

        const devToolsTab = page.locator('.settings-tab-item:has-text("Developer tools")').first();
        await devToolsTab.waitFor({ state: 'visible', timeout: 15_000 });
        await devToolsTab.click({ force: true });

        const enableLogViewer = page.locator(
            '.settings-tab-content .tile-item:has-text("Enable log viewer")',
        ).first();
        await enableLogViewer.waitFor({ state: 'visible', timeout: 10_000 });
        // Initial render is OFF (Model.None); settles to ON (default true).
        // Poll so we don't click during the transition and flip it back to OFF.
        const toggle = enableLogViewer.locator('label.toggle').first();
        for (let i = 0; i < 8; i++) {
            const checked = await toggle.getAttribute('data-input-checked');
            if (checked === 'True') break;
            await enableLogViewer.click({ force: true });
            await page.waitForTimeout(800);
        }

        // Narrow viewport hides the sidebar while a tab is open — back-arrow
        // (SettingsHeader.ArrowClick) returns us to the tab list.
        const tabHeaderBack = page.locator(
            '.settings-tab-header button:has(.icon-arrow-left)',
        ).first();
        if (await tabHeaderBack.isVisible({ timeout: 2000 }).catch(() => false)) {
            await tabHeaderBack.click({ force: true });
            await page.waitForTimeout(500);
        }

        const logViewerTab = page.locator('.settings-tab-item:has-text("Log Viewer")').first();
        await logViewerTab.waitFor({ state: 'visible', timeout: 15_000 });
        await logViewerTab.click({ force: true });

        const tabContent = page.locator('.settings-tab.log-viewer-tab').first();
        await tabContent.waitFor({ state: 'visible', timeout: 10_000 });
        await page.screenshot({ path: shot('opened') });
    }, 60_000);
});
