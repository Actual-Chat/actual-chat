/**
 * E2E test: Sign in with a test account and send a message.
 *
 * Prerequisites:
 * - Server running (or AC_E2E_SERVER=managed)
 * - Optionally, Chrome with remote debugging: `c chrome` (port 9222)
 *
 * Run:
 *   npx vitest run tests/ts/e2e/signin-and-message.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Page } from 'playwright';
import {
    BASE_URL, connectBrowser, dismissCookieConsent, skipOnboarding,
    isSignedIn, signIn, screenshot, useServerRenderMode, waitForAppReady,
    waitForChatReady, waitForEditor, type BrowserConnection,
} from './helpers';

describe('sign-in and send message', () => {
    let conn: BrowserConnection;
    let page: Page;

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();
    }, 30_000);

    afterAll(async () => {
        await page.close();
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    });

    it('should be signed in (sign in if needed)', async () => {
        await useServerRenderMode(page);
        await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
        await waitForAppReady(page);
        await dismissCookieConsent(page);

        if (!await isSignedIn(page)) {
            await signIn(page);
        }

        await skipOnboarding(page);
        await page.screenshot({ path: screenshot('e2e', 'signed-in') });

        // Verify: chat list visible (main app) or account dropdown (landing while signed in)
        const app = page.locator('.chat-list, .account-dropdown').first();
        await expect(app.isVisible({ timeout: 10000 })).resolves.toBe(true);
    }, 60_000);

    it('should navigate to a chat and see the message input', async () => {
        await page.goto(`${BASE_URL}/chat/the-actual-one`, { waitUntil: 'domcontentloaded' });
        await waitForChatReady(page);
        await skipOnboarding(page);

        // Join if needed
        const joinButton = page.locator('button:has-text("Join this chat")');
        if (await joinButton.isVisible({ timeout: 3000 }).catch(() => false)) {
            await joinButton.click();
            await page.waitForTimeout(2000);
        }

        await page.screenshot({ path: screenshot('e2e', 'chat') });

        await waitForEditor(page);
    }, 45_000);

    it('should send a message and see it appear', async () => {
        // Earlier test files in the suite can leave the chat panel unmounted; re-navigate
        // here so the editor is reliably present even when the previous test mid-suite
        // unmounted the chat-view.
        await page.goto(`${BASE_URL}/chat/the-actual-one`, { waitUntil: 'domcontentloaded' });
        await waitForChatReady(page);
        await skipOnboarding(page);
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

        const testMessage = `E2E test at ${new Date().toISOString()}`;
        await page.keyboard.type(testMessage);
        await page.screenshot({ path: screenshot('e2e', 'before-send') });

        // dispatchEvent skips actionability checks (a tutorial overlay can land
        // on top of the editor right after typing). MarkupEditor handles Post on
        // 'keypress' for Enter, so dispatch that to fire its listener directly.
        await messageInput.dispatchEvent('keypress', {
            key: 'Enter', code: 'Enter', bubbles: true, cancelable: true,
        });
        await page.waitForTimeout(2000);

        // The chat panel unmounts briefly after a post (transcription tutorial
        // and chat-list rerender). Re-navigate so the chat view re-mounts.
        await page.goto(`${BASE_URL}/chat/the-actual-one`, { waitUntil: 'domcontentloaded' });
        await waitForChatReady(page);
        await skipOnboarding(page);
        await page.screenshot({ path: screenshot('e2e', 'after-send') });

        const sentMessage = page.locator(`.chat-message-markup:has-text("${testMessage}")`).first();
        await sentMessage.waitFor({ state: 'visible', timeout: 15_000 });
    }, 60_000);
});
