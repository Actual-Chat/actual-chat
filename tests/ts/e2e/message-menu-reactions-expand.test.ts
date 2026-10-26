/**
 * E2E test: expanding the reaction picker in a message's menu keeps the menu next to the message.
 * The picker replaces the menu's content, the menu grows, and the menu host positions it again -
 * which used to put it in the window's top-left corner (#5614). Once for a menu opened with a
 * right-click on the message, once for one opened from the hover menu's "more" button.
 *
 * Prerequisites:
 * - Server running (or AC_E2E_SERVER=managed)
 * - Optionally, Chrome with remote debugging: `ai chrome` (port 9222)
 *
 * Run:
 *   npx vitest run tests/ts/e2e/message-menu-reactions-expand.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Page } from 'playwright';
import {
    connectBrowser, ensureSignedIn, openChat, screenshot, type BrowserConnection,
} from './helpers';

const sourceText = `reactions expand ${Date.now()}`;

interface Box { left: number; top: number; right: number; bottom: number }

/** The picker is wider than the quick row, so the menu may shift to fit - but it still covers the
 *  place the quick row was, rather than jumping away from the message. */
async function expectExpandsInPlace(page: Page, prefix: string) {
    const menu = page.locator('.ac-menu').first();
    await menu.waitFor({ state: 'visible', timeout: 10_000 });
    await menu.evaluate(el => Promise.all(el.getAnimations({ subtree: true }).map(a => a.finished)));
    const getBox = () => menu.evaluate((el): Box => {
        const { left, top, right, bottom } = el.getBoundingClientRect();
        return { left, top, right, bottom };
    });
    const before = await getBox();
    await page.screenshot({ path: screenshot(prefix, '1-menu') });

    await menu.locator('.reaction-select-toggle').click();
    await menu.locator('.reactions-menu').waitFor({ state: 'visible', timeout: 10_000 });
    // The menu is positioned again from a ResizeObserver callback, a frame or two after the render
    await page.waitForTimeout(500);
    const after = await getBox();
    await page.screenshot({ path: screenshot(prefix, '2-expanded') });

    const overlapsBefore = after.left < before.right && after.right > before.left
        && after.top < before.bottom && after.bottom > before.top;
    expect(overlapsBefore, `expanded ${JSON.stringify(after)} vs. ${JSON.stringify(before)}`).toBe(true);
    await page.keyboard.press('Escape');
}

describe('message menu reactions expand', () => {
    let conn: BrowserConnection;
    let page: Page;

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();
        await page.setViewportSize({ width: 1400, height: 900 });
        await ensureSignedIn(page);
        await openChat(page);
        const messageInput = page.locator('#message-input .editor-content[contenteditable="true"]').first();
        await messageInput.click({ force: true });
        await page.waitForTimeout(500);
        await page.keyboard.type(sourceText);
        await page.waitForTimeout(300);
        await messageInput.dispatchEvent('keypress', {
            key: 'Enter', code: 'Enter', bubbles: true, cancelable: true,
        });
        await page.locator(`.chat-message-markup:has-text("${sourceText}")`).first()
            .waitFor({ state: 'visible', timeout: 15_000 });
    }, 90_000);

    afterAll(async () => {
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition -- page may be unset if beforeAll fails
        if (page)
            await page.close();

        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    });

    it('keeps a right-click menu where it opened', async () => {
        await openChat(page);
        const source = page.locator(`.chat-message-markup:has-text("${sourceText}")`).first();
        await source.waitFor({ state: 'visible', timeout: 15_000 });
        await source.click({ button: 'right' });

        await expectExpandsInPlace(page, 'reactions-expand-right-click');
    }, 60_000);

    it('keeps a menu opened from the hover menu next to the message', async () => {
        await openChat(page);
        const source = page.locator(`.chat-message-markup:has-text("${sourceText}")`).first();
        await source.hover();
        const more = page.locator('.message-hover-menu-new [data-menu]').last();
        await more.waitFor({ state: 'visible', timeout: 10_000 });
        await more.click();

        await expectExpandsInPlace(page, 'reactions-expand-hover-more');
    }, 60_000);
});
