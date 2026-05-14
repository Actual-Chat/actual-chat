/**
 * E2E test: Settings > Log Viewer tab header must not overflow the viewport
 * on narrow screens.
 *
 * Regression for the #3730 report button being clipped at the right edge of
 * the screen on iPhone — caused by `box-sizing: content-box` + `w-full` +
 * `px-4` adding the inline padding *beyond* the parent's content box. The
 * web build doesn't register a real ReportUI (MAUI does), so the Report
 * button itself may not render here — but the header layout/CSS is shared,
 * so this still catches the geometry regression.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/log-viewer-report-button.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Page } from 'playwright';
import {
    BASE_URL, connectBrowser, dismissCookieConsent, ensureSignedIn,
    screenshot, waitForAppReady, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e', `log-viewer-${name}`);

describe('Log Viewer tab header fits viewport on narrow screens', () => {
    let conn: BrowserConnection;
    let page: Page;

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();
        await page.setViewportSize({ width: 393, height: 852 });
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
        await page.screenshot({ path: shot('dev-tools-before-toggle') });
        await enableLogViewer.click({ force: true });
        await page.waitForTimeout(800); // LogUI.IsEnabled invalidation propagation
        await page.screenshot({ path: shot('dev-tools-after-toggle') });

        // Narrow viewport hides the sidebar while a tab is open — back-arrow
        // (SettingsHeader.ArrowClick) returns us to the tab list.
        const tabHeaderBack = page.locator(
            '.settings-tab-header button:has(.icon-arrow-left)',
        ).first();
        if (await tabHeaderBack.isVisible({ timeout: 2000 }).catch(() => false)) {
            await tabHeaderBack.click({ force: true });
            await page.waitForTimeout(500);
        }
        await page.screenshot({ path: shot('sidebar') });

        const logViewerTab = page.locator('.settings-tab-item:has-text("Log Viewer")').first();
        await logViewerTab.waitFor({ state: 'visible', timeout: 15_000 });
        await logViewerTab.click({ force: true });

        const tabContent = page.locator('.settings-tab.log-viewer-tab').first();
        await tabContent.waitFor({ state: 'visible', timeout: 10_000 });
        await page.screenshot({ path: shot('opened') });
    }, 60_000);

    it('Log Viewer tab header fits within the viewport (no horizontal overflow)', async () => {
        const viewportWidth = page.viewportSize()!.width;
        const header = page.locator('.log-viewer-tab .settings-tab-header').first();
        await header.waitFor({ state: 'visible' });

        const headerBox = await header.boundingBox();
        expect(headerBox, 'tab header has a bounding box').not.toBeNull();
        const headerRight = headerBox!.x + headerBox!.width;
        expect(
            headerRight,
            `header right edge (${headerRight}) must not exceed viewport width (${viewportWidth})`,
        ).toBeLessThanOrEqual(viewportWidth + 0.5); // half-px rounding slack
    });

    it('every visible header child fits within the viewport', async () => {
        const viewportWidth = page.viewportSize()!.width;
        const overflow = await page.evaluate(width => {
            const header = document.querySelector('.log-viewer-tab .settings-tab-header');
            if (!header)
                return { headerFound: false, overflowing: [] };
            const overflowing: { tag: string; cls: string; right: number; text: string }[] = [];
            for (const el of Array.from(header.children) as HTMLElement[]) {
                if (el.offsetParent === null && el.tagName !== 'BUTTON')
                    continue; // hidden child (e.g. md:hidden back arrow on desktop)
                const r = el.getBoundingClientRect();
                if (r.width === 0 && r.height === 0)
                    continue;
                if (r.right > width + 0.5) {
                    overflowing.push({
                        tag: el.tagName,
                        cls: el.className,
                        right: r.right,
                        text: el.textContent.trim().slice(0, 40),
                    });
                }
            }
            return { headerFound: true, overflowing };
        }, viewportWidth);

        expect(overflow.headerFound, 'settings-tab-header found').toBe(true);
        await page.screenshot({ path: shot('header') });
        expect(
            overflow.overflowing,
            `no header child should extend past viewport (width=${viewportWidth})`,
        ).toEqual([]);
    });

    it('if a Report button is rendered, it is fully visible', async () => {
        const reportBtn = page.locator(
            '.log-viewer-tab .settings-tab-header button:has-text("Report")',
        ).first();
        if (!await reportBtn.isVisible({ timeout: 1500 }).catch(() => false)) {
            // ReportUI base is a no-op on web; the button only renders on hosts
            // that register a real ReportUI (e.g. MAUI's MauiReportUI). Skip.
            return;
        }
        const viewportWidth = page.viewportSize()!.width;
        const box = await reportBtn.boundingBox();
        expect(box, 'Report button has a bounding box').not.toBeNull();
        const right = box!.x + box!.width;
        expect(
            right,
            `Report button right edge (${right}) must be within viewport (${viewportWidth})`,
        ).toBeLessThanOrEqual(viewportWidth + 0.5);
    });
});
