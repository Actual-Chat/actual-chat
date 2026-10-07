/**
 * E2E test: the chat lays out around a phone's safe areas and rounded corners (#4826).
 *
 * The page runs as an iPhone 15 - its viewport, real env(safe-area-inset-*) values and the display's
 * corner radius - and the chat chrome must keep every control inside the safe area and out of the
 * corners. A second pass does the same in landscape and on a Pixel 8 with the gesture bar.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/safe-areas.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Page } from 'playwright';
import {
    clearBrowserCache, connectBrowser, ensureSignedIn, openChat, screenshot, type BrowserConnection,
} from './helpers';
import {
    emulateSafeAreas, expectInsideSafeArea, findSafeAreaViolations, SafeAreas, type SafeAreaPreset,
} from './safe-areas';

const Presets = SafeAreas.Presets;

const shot = (name: string) => screenshot('e2e', `safe-areas-${name}`);

describe('safe areas', () => {
    let conn: BrowserConnection;

    beforeAll(async () => {
        conn = await connectBrowser();
    }, 60_000);

    afterAll(async () => {
        if (conn.ownsBrowser) {
            await conn.context.close().catch(() => { /* ignore */ });
            await conn.browser.close().catch(() => { /* ignore */ });
        }
    });

    async function newPhonePage(preset: SafeAreaPreset): Promise<Page> {
        const page = await conn.context.newPage();
        await emulateSafeAreas(page, preset);
        await clearBrowserCache(page);
        await ensureSignedIn(page);
        await openChat(page);
        return page;
    }

    it('feeds the real env() insets into the safe-area variables', async () => {
        const preset = Presets.iphone15;
        const page = await newPhonePage(preset);
        try {
            // act
            const vars = await page.evaluate(() => {
                const style = getComputedStyle(document.body);
                return ['top', 'right', 'bottom', 'left']
                    .map(side => style.getPropertyValue(`--safe-area-${side}`).trim());
            });

            // assert
            expect(vars).toEqual(['59px', '0px', '34px', '0px']);
        } finally {
            await page.close().catch(() => { /* ignore */ });
        }
    }, 120_000);

    it('keeps the chat header and editor controls on an iPhone 15 screen', async () => {
        const preset = Presets.iphone15;
        const page = await newPhonePage(preset);
        try {
            // act
            await page.screenshot({ path: shot('iphone15') });

            // assert — the header's controls sit below the status bar
            const headerControls = page.locator('.layout-header').first().locator('button, a[href]');
            await expectInsideSafeArea(headerControls.first(), preset, 'first header control');
            await expectInsideSafeArea(headerControls.last(), preset, 'last header control');

            // assert — the editor's controls sit above the home indicator and out of the corners
            const editor = page.locator('.chat-message-editor').first();
            await expectInsideSafeArea(editor.locator('.attach-btn').first(), preset, 'attach button');
            await expectInsideSafeArea(editor.locator('.editor-content').first(), preset, 'editor');
            await expectInsideSafeArea(editor.locator('button:visible').last(), preset, 'last editor button');

            // assert — nothing interactive anywhere on the page is under an inset or in a corner
            expect(await findSafeAreaViolations(page, preset)).toEqual([]);
        } finally {
            await page.close().catch(() => { /* ignore */ });
        }
    }, 180_000);

    it.each([
        ['iphone15Landscape', Presets.iphone15Landscape],
        ['pixel8', Presets.pixel8],
    ] as const)('keeps every control on the %s screen', async (name, preset) => {
        const page = await newPhonePage(preset);
        try {
            // act
            await page.screenshot({ path: shot(name) });
            const violations = await findSafeAreaViolations(page, preset);

            // assert
            expect(violations).toEqual([]);
        } finally {
            await page.close().catch(() => { /* ignore */ });
        }
    }, 180_000);
});
