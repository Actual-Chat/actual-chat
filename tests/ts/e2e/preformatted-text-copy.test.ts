/**
 * E2E test: clicking inline preformatted text (`code`) copies it to the
 * clipboard, and the cursor is `pointer` on mouse devices.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/preformatted-text-copy.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Page } from 'playwright';
import {
    BASE_URL, connectBrowser, ensureSignedIn, screenshot,
    skipOnboarding, waitForChatReady, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e', `preformatted-${name}`);

describe('preformatted text markup copy-on-click', () => {
    let conn: BrowserConnection;
    let page: Page;
    let preformattedText: string;

    beforeAll(async () => {
        conn = await connectBrowser();
        await conn.context.grantPermissions(['clipboard-read', 'clipboard-write'], { origin: BASE_URL });
        page = await conn.context.newPage();
        await ensureSignedIn(page);
    }, 120_000);

    afterAll(async () => {
        await page.close();
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    });

    it('sends a chat message containing preformatted text', async () => {
        await page.goto(`${BASE_URL}/chat/the-actual-one`, { waitUntil: 'domcontentloaded' });
        await waitForChatReady(page);
        await skipOnboarding(page);

        const joinButton = page.locator('button:has-text("Join this chat")');
        if (await joinButton.isVisible({ timeout: 3000 }).catch(() => false)) {
            await joinButton.click();
            await page.waitForTimeout(2000);
        }

        const messageInput = page.locator('#message-input .editor-content[contenteditable="true"]').first();
        await messageInput.waitFor({ state: 'visible', timeout: 15_000 });
        await messageInput.click();
        await page.waitForTimeout(300);

        preformattedText = `copy-test-${Date.now()}`;
        await page.keyboard.type(`Here is \`${preformattedText}\` inline.`);
        await page.screenshot({ path: shot('before-send') });

        const sendButton = page.locator('button.post-message');
        if (await sendButton.isVisible({ timeout: 3000 }).catch(() => false)) {
            await sendButton.click();
        } else {
            await page.keyboard.press('Control+Enter');
        }
        await page.waitForTimeout(3000);

        const code = page.locator(`code.preformatted-text-markup:has-text("${preformattedText}")`).first();
        await code.waitFor({ state: 'visible', timeout: 15_000 });
        await page.screenshot({ path: shot('after-send') });
    }, 60_000);

    it('shows pointer cursor on the preformatted text', async () => {
        const code = page.locator(`code.preformatted-text-markup:has-text("${preformattedText}")`).first();
        const cursor = await code.evaluate(el => getComputedStyle(el).cursor);
        expect(cursor).toBe('pointer');
    });

    it('copies the preformatted text to the clipboard on click', async () => {
        const code = page.locator(`code.preformatted-text-markup:has-text("${preformattedText}")`).first();
        await code.click();
        // CopyTrigger adds `.copied` to its wrapper for 3s after a successful copy.
        await page.locator('.copy-trigger.copied').first()
            .waitFor({ state: 'visible', timeout: 5_000 })
            .catch(() => { /* hint may auto-clear before we observe it */ });

        const clipboardText = await page.evaluate(() => navigator.clipboard.readText());
        expect(clipboardText).toBe(preformattedText);
    }, 30_000);
});
