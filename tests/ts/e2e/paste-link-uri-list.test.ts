/**
 * E2E test: a link copied on iOS arrives as both text/plain and text/uri-list - it's pasted once,
 * while a page title shared from Safari together with its URL keeps both.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/paste-link-uri-list.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Locator, Page } from 'playwright';
import { connectBrowser, ensureSignedIn, openChat, screenshot, type BrowserConnection } from './helpers';

const shot = (name: string) => screenshot('e2e', `paste-link-uri-list-${name}`);

describe('pasting a link that carries text/uri-list', () => {
    let conn: BrowserConnection;
    let page: Page;
    let editor: Locator;
    const id = Date.now();
    const url = `https://youtu.be/_1hMjKT6O6M?is=wdKfdodM8VmLqDD1&e2e=${id}`;

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();
        await ensureSignedIn(page);
        await openChat(page);
        editor = page.locator('#message-input .editor-content[contenteditable="true"]').first();
    }, 120_000);

    afterAll(async () => {
        await clearEditor();
        await page.close();
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    });

    it('inserts the link once when text/plain is the same link', async () => {
        // act
        await paste({ 'text/plain': url, 'text/uri-list': url });
        await page.screenshot({ path: shot('same-link') });

        // assert
        expect(await editorText()).toBe(url);
    }, 30_000);

    it('keeps the shared title along with its link', async () => {
        // arrange
        const title = `Shared page title ${id}`;

        // act
        await paste({ 'text/plain': title, 'text/uri-list': url });
        await page.screenshot({ path: shot('title-and-link') });

        // assert
        expect((await editorText()).split(/\n+/)).toEqual([title, url]);
    }, 30_000);

    async function paste(data: Record<string, string>): Promise<void> {
        await clearEditor();
        await editor.click({ force: true });
        await editor.evaluate((el, data) => {
            const clipboardData = new DataTransfer();
            for (const [type, value] of Object.entries(data))
                clipboardData.setData(type, value);
            el.dispatchEvent(new ClipboardEvent('paste', { clipboardData, bubbles: true, cancelable: true }));
        }, data);
        await page.waitForTimeout(500);
    }

    async function clearEditor(): Promise<void> {
        // ChatMessageEditor restores per-chat drafts, so a previous paste would precede ours
        await editor.click({ force: true });
        await editor.evaluate(el => {
            el.innerHTML = '';
            el.dispatchEvent(new Event('input', { bubbles: true }));
        });
    }

    async function editorText(): Promise<string> {
        return (await editor.evaluate(el => (el as HTMLElement).innerText)).trim();
    }
});
