/**
 * E2E test: selecting part of a message and opening its context menu offers "Quote",
 * which replies with only the selected fragment in the quote block. #4274
 *
 * Prerequisites:
 * - Server running (or AC_E2E_SERVER=managed)
 * - Optionally, Chrome with remote debugging: `ai chrome` (port 9222)
 *
 * Run:
 *   npx vitest run tests/ts/e2e/quote-selected-text.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Page } from 'playwright';
import {
    BASE_URL, connectBrowser, ensureSignedIn, skipOnboarding,
    screenshot, waitForChatReady, waitForEditor, type BrowserConnection,
} from './helpers';

const CHAT_URL = `${BASE_URL}/chat/the-actual-one`;
const stamp = Date.now();
const sourceText = `quote source ${stamp} keep this part out`;
const fragment = `source ${stamp} keep`;
const replyText = `quoting ${stamp}`;

async function openChat(page: Page) {
    await page.goto(CHAT_URL, { waitUntil: 'domcontentloaded' });
    await waitForChatReady(page);
    await skipOnboarding(page);
}

async function typeAndSend(page: Page, text: string) {
    await waitForEditor(page);
    const messageInput = page.locator('#message-input .editor-content[contenteditable="true"]').first();
    await messageInput.click({ force: true });
    await page.waitForTimeout(200);
    await messageInput.evaluate(el => {
        el.innerHTML = '';
        el.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await messageInput.click({ force: true });
    await page.keyboard.type(text);
    await messageInput.dispatchEvent('keypress', {
        key: 'Enter', code: 'Enter', bubbles: true, cancelable: true,
    });
    await page.waitForTimeout(2000);
}

// The menu fades in over 200ms (ac-menu-show); a screenshot taken as soon as an item is
// "visible" catches it half-transparent.
async function waitForMenuShown(page: Page) {
    await page.locator('.ac-menu').first().evaluate(el =>
        Promise.all(el.getAnimations({ subtree: true }).map(a => a.finished)));
}

describe('quote selected text', () => {
    let conn: BrowserConnection;
    let page: Page;

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();
        await ensureSignedIn(page);
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

    it('posts the source message', async () => {
        await openChat(page);

        const joinButton = page.locator('button:has-text("Join this chat")');
        if (await joinButton.isVisible({ timeout: 3000 }).catch(() => false)) {
            await joinButton.click();
            await page.waitForTimeout(2000);
        }

        await typeAndSend(page, sourceText);
        await openChat(page);
        const source = page.locator(`.chat-message-markup:has-text("${sourceText}")`).first();
        await source.waitFor({ state: 'visible', timeout: 15_000 });
        await page.screenshot({ path: screenshot('quote', '01-source-posted') });
    }, 90_000);

    it('offers Quote for a selection and sends a reply carrying only the fragment', async () => {
        await openChat(page);
        const source = page.locator(`.chat-message-markup:has-text("${sourceText}")`).first();
        await source.waitFor({ state: 'visible', timeout: 15_000 });

        // Select the fragment inside the message, then open the context menu the way the
        // menu host listens for it: a contextmenu event bubbling up from the message.
        const selected = await source.evaluate((el, fragment) => {
            const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
            for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                const start = node.textContent?.indexOf(fragment) ?? -1;
                if (start < 0)
                    continue;

                const range = document.createRange();
                range.setStart(node, start);
                range.setEnd(node, start + fragment.length);
                const selection = window.getSelection()!;
                selection.removeAllRanges();
                selection.addRange(range);
                const rect = range.getBoundingClientRect();
                el.dispatchEvent(new MouseEvent('contextmenu', {
                    bubbles: true,
                    cancelable: true,
                    clientX: rect.left + rect.width / 2,
                    clientY: rect.top + rect.height / 2,
                }));
                return selection.toString();
            }
            return null;
        }, fragment);
        expect(selected).toBe(fragment);

        const quoteItem = page.locator('li.ac-menu-item:has-text("Quote")').first();
        await quoteItem.waitFor({ state: 'visible', timeout: 10_000 });
        await waitForMenuShown(page);
        await page.screenshot({ path: screenshot('quote', '02-menu') });
        await quoteItem.click();

        const panelQuote = page.locator('.related-chat-entry .quote-text').first();
        await panelQuote.waitFor({ state: 'visible', timeout: 10_000 });
        await expect.poll(() => panelQuote.textContent(), { timeout: 10_000 }).toBe(fragment);
        await page.screenshot({ path: screenshot('quote', '03-editor-panel') });

        await typeAndSend(page, replyText);
        await openChat(page);
        const reply = page.locator(`[data-chat-entry-id]:has(.chat-message-markup:has-text("${replyText}"))`).first();
        await reply.waitFor({ state: 'visible', timeout: 15_000 });
        const quoteBlock = reply.locator('.chat-message-quote .c-text').first();
        await quoteBlock.waitFor({ state: 'visible', timeout: 10_000 });
        await expect.poll(() => quoteBlock.textContent(), { timeout: 10_000 }).toBe(fragment);
        await page.screenshot({ path: screenshot('quote', '04-reply-rendered') });
    }, 120_000);

    it('offers no Quote when nothing is selected', async () => {
        await openChat(page);
        const source = page.locator(`.chat-message-markup:has-text("${sourceText}")`).first();
        await source.waitFor({ state: 'visible', timeout: 15_000 });
        await source.evaluate(el => {
            window.getSelection()?.removeAllRanges();
            const rect = el.getBoundingClientRect();
            el.dispatchEvent(new MouseEvent('contextmenu', {
                bubbles: true,
                cancelable: true,
                clientX: rect.left + 10,
                clientY: rect.top + 10,
            }));
        });

        const replyItem = page.locator('li.ac-menu-item:has-text("Reply")').first();
        await replyItem.waitFor({ state: 'visible', timeout: 10_000 });
        await waitForMenuShown(page);
        await page.screenshot({ path: screenshot('quote', '05-menu-without-selection') });
        expect(await page.locator('li.ac-menu-item:has-text("Quote")').count()).toBe(0);
        await page.keyboard.press('Escape');
    }, 60_000);
});
