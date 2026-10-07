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

    it('keeps the coach panel header below the status bar on an iPhone 15 screen', async () => {
        const preset = Presets.iphone15;
        const page = await newPhonePage(preset);
        try {
            // act — the chat side panel first: its header is the reference the coach's has to match
            await page.locator('.chat-header-center').first().click();
            const modes = page.locator('.right-panel-mode-switch .btn-mode');
            await modes.first().click();
            const chatHeader = page.locator('.chat-side-panel .c-header').first();
            await chatHeader.waitFor({ state: 'visible', timeout: 15_000 });
            // The side panel slides in and the swap wipes; wait until the header stops moving
            await page.waitForTimeout(1_000);
            const chatClose = await chatHeader.locator('.right-panel-close-btn').boundingBox();
            const chatCover = await chatHeader.locator('> .c-top').boundingBox();
            const chatTitle = await chatHeader.locator('.c-bottom .c-title').boundingBox();
            await page.screenshot({ path: shot('iphone15-side-panel-chat') });

            await modes.last().click();
            const header = page.locator('.coach-header').first();
            await header.waitFor({ state: 'visible', timeout: 15_000 });
            await page.waitForTimeout(1_000);
            await page.screenshot({ path: shot('iphone15-coach') });

            // assert — the close button sits below the status bar and out of the corner
            await expectInsideSafeArea(header.locator('.right-panel-close-btn'), preset, 'coach close button');
            await expectInsideSafeArea(header.locator('.c-settings-btn'), preset, 'coach settings button');

            // assert — switching Chat/Coach moves neither the close button, nor the cover, nor the title line
            expect(await header.locator('.right-panel-close-btn').boundingBox()).toEqual(chatClose);
            expect(await header.locator('.c-cover').boundingBox()).toEqual(chatCover);
            const title = await header.locator('.c-title').boundingBox();
            expect(title!.y).toBe(chatTitle!.y);

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
