/**
 * E2E test: text whose lines are separated by U+2028 / U+2029 instead of "\n" - what Apple Notes
 * puts on the clipboard for soft line breaks - is pasted as separate lines and posted in full.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/paste-line-separators.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Locator, Page } from 'playwright';
import { connectBrowser, ensureSignedIn, openChat, screenshot, type BrowserConnection } from './helpers';

const shot = (name: string) => screenshot('e2e', `paste-line-separators-${name}`);

describe('pasting text with Unicode line separators', () => {
    let conn: BrowserConnection;
    let page: Page;
    let editor: Locator;
    const id = Date.now();
    const lines = [`Notes line one ${id}`, `Notes line two ${id}`, `Notes line three ${id}`];

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();
        await ensureSignedIn(page);
        await openChat(page);
        editor = page.locator('#message-input .editor-content[contenteditable="true"]').first();
    }, 120_000);

    afterAll(async () => {
        await page.close();
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    });

    it('shows every pasted line in the editor', async () => {
        await editor.click({ force: true });
        // ChatMessageEditor restores per-chat drafts, so a previous run's unsent text would precede ours
        await editor.evaluate(el => {
            el.innerHTML = '';
            el.dispatchEvent(new Event('input', { bubbles: true }));
        });
        await editor.click({ force: true });

        await editor.evaluate((el, text) => {
            const clipboardData = new DataTransfer();
            clipboardData.setData('text/plain', text);
            el.dispatchEvent(new ClipboardEvent('paste', { clipboardData, bubbles: true, cancelable: true }));
        }, `${lines[0]} ${lines[1]} ${lines[2]}`);
        await page.waitForTimeout(500);
        await page.screenshot({ path: shot('editor') });

        const editorText = await editor.evaluate(el => (el as HTMLElement).innerText);
        expect(editorText.trim().split(/\n+/)).toEqual(lines);
    }, 30_000);

    it('posts every pasted line', async () => {
        await editor.dispatchEvent('keypress', {
            key: 'Enter', code: 'Enter', bubbles: true, cancelable: true,
        });

        const message = page.locator(`.chat-message-markup:has-text("${lines[0]}")`).first();
        await message.waitFor({ state: 'visible', timeout: 15_000 });
        await message.scrollIntoViewIfNeeded();
        await page.waitForTimeout(1_000);
        await page.screenshot({ path: shot('posted') });

        const messageText = await message.evaluate(el => (el as HTMLElement).innerText);
        expect(messageText.trim().split(/\n+/)).toEqual(lines);
    }, 30_000);
});
