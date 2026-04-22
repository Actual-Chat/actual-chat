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
    isSignedIn, signIn, screenshot, type BrowserConnection,
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
        await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
        await page.waitForTimeout(2000);
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
        await page.waitForTimeout(3000);
        await skipOnboarding(page);

        // Join if needed
        const joinButton = page.locator('button:has-text("Join this chat")');
        if (await joinButton.isVisible({ timeout: 3000 }).catch(() => false)) {
            await joinButton.click();
            await page.waitForTimeout(2000);
        }

        await page.screenshot({ path: screenshot('e2e', 'chat') });

        const messageInput = page.locator('.message-input[contenteditable="true"], #message-input').first();
        await expect(messageInput.isVisible({ timeout: 10000 })).resolves.toBe(true);
    }, 30_000);

    it('should send a message and see it appear', async () => {
        const messageInput = page.locator('.message-input[contenteditable="true"], #message-input').first();
        await messageInput.click();
        await page.waitForTimeout(300);

        const testMessage = `E2E test at ${new Date().toISOString()}`;
        await page.keyboard.type(testMessage);
        await page.screenshot({ path: screenshot('e2e', 'before-send') });

        // Send
        const sendButton = page.locator('button.post-message');
        if (await sendButton.isVisible({ timeout: 3000 }).catch(() => false)) {
            await sendButton.click();
        } else {
            await page.keyboard.press('Control+Enter');
        }
        await page.waitForTimeout(3000);
        await page.screenshot({ path: screenshot('e2e', 'after-send') });

        // Verify the message appears in the chat (text is inside .chat-message-markup)
        const sentMessage = page.locator(`.chat-message-markup:has-text("${testMessage}")`).first();
        await expect(sentMessage.isVisible({ timeout: 10000 })).resolves.toBe(true);
    }, 30_000);
});
