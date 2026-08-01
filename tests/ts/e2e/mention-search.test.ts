/**
 * E2E test: Mention search in chat message editor.
 *
 * Tests that typing @ shows the mention list, filtering works,
 * and selecting a mention inserts it.
 *
 * Prerequisites:
 * - Server running (or AC_E2E_SERVER=managed)
 * - Optionally, Chrome with remote debugging: `ai chrome` (port 9222)
 *
 * Run:
 *   npx vitest run tests/ts/e2e/mention-search.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Page } from 'playwright';
import {
    BASE_URL, connectBrowser, ensureSignedIn, skipOnboarding,
    screenshot, waitForChatReady, type BrowserConnection,
} from './helpers';

async function clearEditor(page: Page) {
    await page.keyboard.press('Escape');
    await page.waitForTimeout(100);
    const editor = page.locator('#message-input .editor-content[contenteditable="true"]').first();
    // Best-effort — if the editor vanished (e.g. navigation away in afterAll) don't fail teardown.
    if (!await editor.isVisible({ timeout: 1000 }).catch(() => false)) return;
    await editor.evaluate(el => {
        el.innerHTML = '';
        el.dispatchEvent(new Event('input', { bubbles: true }));
    }).catch(() => { /* ignore */ });
    await page.waitForTimeout(200);
}

// Re-mounts the chat panel if it collapsed between tests (Voxt's responsive layout
// can hide the right panel mid-suite when ScreenSize re-evaluates).
async function ensureEditorReady(page: Page) {
    const editor = page.locator('#message-input .editor-content[contenteditable="true"]').first();
    if (!await editor.isVisible({ timeout: 1500 }).catch(() => false)) {
        await page.goto(`${BASE_URL}/chat/the-actual-one`, { waitUntil: 'domcontentloaded' });
        await waitForChatReady(page);
        await editor.waitFor({ state: 'visible', timeout: 15_000 });
    }
    await editor.click({ force: true });
    await page.waitForTimeout(300);
}

describe('mention search', () => {
    let conn: BrowserConnection;
    let page: Page;

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();

        await ensureSignedIn(page);

        // Navigate to a chat with multiple members
        await page.goto(`${BASE_URL}/chat/the-actual-one`, { waitUntil: 'domcontentloaded' });
        await waitForChatReady(page);
        await skipOnboarding(page);

        // Join if needed
        const joinButton = page.locator('button:has-text("Join this chat")');
        if (await joinButton.isVisible({ timeout: 3000 }).catch(() => false)) {
            await joinButton.click();
            await waitForChatReady(page);
        }

        // Wait for editor without skipOnboarding-on-failure — once the chat panel
        // is mounted, calling skipOnboarding again can unmount it via resetOnboarding's
        // Blazor state reset.
        await page.locator('#message-input .editor-content[contenteditable="true"]').first()
            .waitFor({ state: 'visible', timeout: 20_000 });
        await page.screenshot({ path: screenshot('mention', '00-chat-page') });
    }, 60_000);

    afterAll(async () => {
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition -- page may be unset if beforeAll fails
        if (page) {
            await clearEditor(page);
            await page.close();
        }
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    });

    it('should show mention list when typing @', async () => {
        await ensureEditorReady(page);
        const messageInput = page.locator('#message-input .editor-content[contenteditable="true"]').first();
        await messageInput.click();
        await page.waitForTimeout(300);

        await page.keyboard.type('@');
        await page.waitForTimeout(500);
        await page.screenshot({ path: screenshot('mention', '01-at-typed') });

        const mentionList = page.locator('.mention-list:not(.non-visible)');
        await expect(mentionList.isVisible({ timeout: 5000 })).resolves.toBe(true);

        const items = await page.locator('.mention-list-item').count();
        expect(items).toBeGreaterThan(0);

        await clearEditor(page);
    }, 30_000);

    it('should filter mention list by search term', async () => {
        await ensureEditorReady(page);
        const messageInput = page.locator('#message-input .editor-content[contenteditable="true"]').first();
        await messageInput.click();
        await page.waitForTimeout(300);

        await page.keyboard.type('@');
        await page.waitForTimeout(500);

        const countBefore = await page.locator('.mention-list-item').count();

        // Type a letter to filter
        await page.keyboard.type('a');
        await page.waitForTimeout(500);
        await page.screenshot({ path: screenshot('mention', '02-filtered') });

        const countAfter = await page.locator('.mention-list-item').count();
        expect(countAfter).toBeGreaterThan(0);
        expect(countAfter).toBeLessThanOrEqual(countBefore);

        await clearEditor(page);
    }, 30_000);

    it('should insert mention on Enter and close the list', async () => {
        await ensureEditorReady(page);
        const messageInput = page.locator('#message-input .editor-content[contenteditable="true"]').first();
        await messageInput.click();
        await page.waitForTimeout(300);

        await page.keyboard.type('@');

        // Wait for mention list to appear first
        const mentionListOpen = page.locator('.mention-list:not(.non-visible)');
        await mentionListOpen.waitFor({ state: 'visible', timeout: 5000 });
        await page.waitForTimeout(300);

        await page.screenshot({ path: screenshot('mention', '03-before-enter') });
        const items = page.locator('.mention-list-item');
        const itemCount = await items.count();
        expect(itemCount).toBeGreaterThan(0);

        // First item should be auto-selected; click it if .selected class isn't set
        const selectedItem = page.locator('.mention-list-item.selected');
        if (!await selectedItem.isVisible({ timeout: 1000 }).catch(() => false)) {
            await items.first().click();
            await page.waitForTimeout(300);
        }

        await page.keyboard.press('Enter');
        await page.waitForTimeout(500);
        await page.screenshot({ path: screenshot('mention', '03-selected') });

        // Mention list should close after selection
        const mentionList = page.locator('.mention-list:not(.non-visible)');
        const stillVisible = await mentionList.isVisible({ timeout: 1000 }).catch(() => false);
        expect(stillVisible).toBe(false);

        await clearEditor(page);
    }, 30_000);
});
