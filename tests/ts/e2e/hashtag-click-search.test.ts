/**
 * E2E test: a #hashtag in a posted message renders as clickable markup, and
 * clicking it opens the left-panel search with the tag as the query and the
 * Messages type filter applied. #4121
 *
 * Prerequisites:
 * - Server running (or AC_E2E_SERVER=managed)
 * - Optionally, Chrome with remote debugging: `ai chrome` (port 9222)
 *
 * Run:
 *   npx vitest run tests/ts/e2e/hashtag-click-search.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Page } from 'playwright';
import {
    BASE_URL, connectBrowser, ensureSignedIn, skipOnboarding,
    screenshot, waitForChatReady, waitForEditor, type BrowserConnection,
} from './helpers';

const CHAT_URL = `${BASE_URL}/chat/the-actual-one`;
const tag = `#e2etag${Date.now()}`;

async function openChat(page: Page) {
    await page.goto(CHAT_URL, { waitUntil: 'domcontentloaded' });
    await waitForChatReady(page);
    await skipOnboarding(page);
}

describe('hashtag click-to-search', () => {
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

    it('posts a message with a hashtag and renders it as hashtag markup', async () => {
        await openChat(page);

        const joinButton = page.locator('button:has-text("Join this chat")');
        if (await joinButton.isVisible({ timeout: 3000 }).catch(() => false)) {
            await joinButton.click();
            await page.waitForTimeout(2000);
        }

        await waitForEditor(page);
        const messageInput = page.locator('#message-input .editor-content[contenteditable="true"]').first();
        await messageInput.click({ force: true });
        await page.waitForTimeout(200);
        // Clear leftover draft: ChatMessageEditor restores per-chat drafts across page loads,
        // so a previous run's typed-but-unsent text appends to ours otherwise.
        await messageInput.evaluate(el => {
            el.innerHTML = '';
            el.dispatchEvent(new Event('input', { bubbles: true }));
        });
        await messageInput.click({ force: true });
        await page.keyboard.type(`checking ${tag} rendering`);
        await page.screenshot({ path: screenshot('hashtag', '01-before-send') });

        // dispatchEvent skips actionability checks (a tutorial overlay can land on top of
        // the editor right after typing); MarkupEditor posts on the Enter 'keypress'.
        await messageInput.dispatchEvent('keypress', {
            key: 'Enter', code: 'Enter', bubbles: true, cancelable: true,
        });
        await page.waitForTimeout(2000);

        // The chat panel can unmount briefly after a post; re-navigate to re-mount it.
        await openChat(page);
        const hashtag = page.locator(`.chat-message-markup .hashtag-markup:has-text("${tag}")`).first();
        await hashtag.waitFor({ state: 'visible', timeout: 15_000 });
        await page.screenshot({ path: screenshot('hashtag', '02-rendered') });
    }, 90_000);

    it('clicking the hashtag opens search with the tag and the Messages filter', async () => {
        await openChat(page);
        const hashtag = page.locator(`.chat-message-markup .hashtag-markup:has-text("${tag}")`).first();
        await hashtag.waitFor({ state: 'visible', timeout: 15_000 });
        await hashtag.click({ force: true });
        await page.screenshot({ path: screenshot('hashtag', '03-after-click') });

        const searchPanel = page.locator('.left-chat-search-input.open').first();
        await searchPanel.waitFor({ state: 'visible', timeout: 10_000 });

        const input = page.locator('.left-chat-search-input input[type="text"]').first();
        await expect.poll(() => input.inputValue(), { timeout: 10_000 }).toBe(tag);

        const messagesBadge = page
            .locator('.left-chat-search-input .search-filter-badge:has-text("Messages")')
            .first();
        await messagesBadge.waitFor({ state: 'visible', timeout: 10_000 });
        await page.screenshot({ path: screenshot('hashtag', '04-search-open') });
    }, 60_000);
});
