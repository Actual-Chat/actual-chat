/**
 * E2E test: a non-media file attachment's menu opens the share modal, which sends the file
 * as a new message via the chat picker - the same dialog the visual media viewer uses.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/share-file-menu.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, beforeAll, afterAll, expect } from 'vitest';
import type { Locator, Page } from 'playwright';
import {
    DEFAULT_CHAT_URL, connectBrowser, ensureSignedIn, openChat, skipOnboarding, screenshot,
    waitForChatReady, withUILanguage, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e', name);

/** Types the tag and confirms it landed: the editor re-renders around a freshly attached file,
 *  and keystrokes sent while it does are lost. */
async function typeTag(page: Page, editor: Locator, tag: string) {
    for (let attempt = 0; attempt < 3; attempt++) {
        await editor.click({ force: true });
        await editor.evaluate(el => {
            el.innerHTML = '';
            el.dispatchEvent(new Event('input', { bubbles: true }));
        });
        await editor.click({ force: true });
        await page.keyboard.type(tag);
        if ((await editor.innerText()).includes(tag))
            return;

        await page.waitForTimeout(1_000);
    }
    throw new Error(`typeTag: "${tag}" never landed in the editor`);
}

describe('share a file from its menu', () => {
    let conn: BrowserConnection;
    let page: Page;
    const tag = `share-file-${Date.now().toString(36)}`;

    beforeAll(async () => {
        conn = await connectBrowser();
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

    it('opens the share modal from the file attachment menu and sends the file', async () => {
        // arrange - post a message with a plain-text attachment and a unique marker tag
        await openChat(page);
        const fileInput = page.locator('input.attachment-web-file-picker').first();
        await fileInput.waitFor({ state: 'attached', timeout: 5_000 });
        await fileInput.setInputFiles({
            name: `${tag}.txt`,
            mimeType: 'text/plain',
            buffer: Buffer.from(`shared file ${tag}\n`),
        });
        await page.locator('.attachment-list-wrapper').first()
            .waitFor({ state: 'visible', timeout: 15_000 });
        await page.waitForTimeout(2_000);

        const messageInput = page.locator('#message-input .editor-content[contenteditable="true"]').first();
        await typeTag(page, messageInput, tag);
        await messageInput.dispatchEvent('keypress', {
            key: 'Enter', code: 'Enter', bubbles: true, cancelable: true,
        });

        await page.waitForTimeout(2_000);
        await page.goto(withUILanguage(DEFAULT_CHAT_URL), { waitUntil: 'domcontentloaded' });
        await waitForChatReady(page);
        await skipOnboarding(page);

        const postedEntry = page
            .locator(`[data-chat-entry-id]:has(.chat-message-markup:has-text("${tag}"))`)
            .first();
        await postedEntry.waitFor({ state: 'visible', timeout: 30_000 });
        await page.screenshot({ path: shot('share-file-posted') });

        // act - open the file attachment's menu
        const menuButton = postedEntry.locator('.file-attachment button[data-menu]').first();
        await menuButton.waitFor({ state: 'attached', timeout: 15_000 });
        await menuButton.click({ force: true });
        const menu = page.locator('.ac-menu-host .ac-menu').first();
        await menu.waitFor({ state: 'visible', timeout: 10_000 });
        await page.screenshot({ path: shot('share-file-menu-open') });

        // assert - the menu offers the share, and the in-app forward is gone
        const shareItem = menu.locator('li.ac-menu-item:has-text("Share file")').first();
        await shareItem.waitFor({ state: 'visible', timeout: 5_000 });

        // act - open the share modal
        await shareItem.click();
        const shareModal = page.locator('.share-modal').first();
        await shareModal.waitFor({ state: 'visible', timeout: 10_000 });
        await page.waitForTimeout(1_000);
        await page.screenshot({ path: shot('share-file-modal-open') });
        expect(await page.locator('.forward-message-modal').count()).toBe(0);

        // act - pick the first contact and send
        const firstContact = shareModal.locator('.contact-selector-list-item').first();
        await firstContact.waitFor({ state: 'visible', timeout: 10_000 });
        await firstContact.click();
        await page.waitForTimeout(300);
        const sendBtn = shareModal.locator('button:has-text("Send to selected contacts"):not([disabled])').first();
        await sendBtn.waitFor({ state: 'visible', timeout: 5_000 });
        await sendBtn.click();

        // assert - the modal closes after sharing
        await shareModal.waitFor({ state: 'hidden', timeout: 15_000 });
        await page.screenshot({ path: shot('share-file-modal-closed') });
    }, 180_000);

    it('opens the share modal from the Files tab item menu', async () => {
        // arrange - open the chat's right panel on its Files tab
        await openChat(page);
        const panelToggle = page.locator('button.btn-h:has(i.icon-layout)').first();
        if (await panelToggle.count() > 0)
            await panelToggle.evaluate(el => (el as HTMLElement).click());
        await page.locator('.chat-side-panel').first().waitFor({ state: 'attached', timeout: 15_000 });
        await page.waitForTimeout(1_000);
        await page.screenshot({ path: shot('share-file-tab-panel') });
        // The side panel slides in, so the tab is off-viewport for Playwright's own click
        const filesTab = page.locator('.chat-side-panel .btn-tab:has-text("Files")').first();
        await filesTab.waitFor({ state: 'attached', timeout: 15_000 });
        await filesTab.evaluate(el => (el as HTMLElement).click());
        // Any file does: the tab lists the chat's indexed files, and the one just posted may
        // not be indexed yet
        const fileRow = page.locator('.chat-side-panel .c-file-row').first();
        await fileRow.waitFor({ state: 'visible', timeout: 30_000 });

        // act - the item menu opens on a secondary click
        await fileRow.click({ button: 'right' });
        const menu = page.locator('.ac-menu-host .ac-menu').first();
        await menu.waitFor({ state: 'visible', timeout: 10_000 });
        await page.screenshot({ path: shot('share-file-tab-menu-open') });
        const shareItem = menu.locator('li.ac-menu-item:has-text("Share")').first();
        await shareItem.waitFor({ state: 'visible', timeout: 5_000 });
        await shareItem.click();

        // assert - the same share modal opens, titled for a file
        const shareModal = page.locator('.share-modal').first();
        await shareModal.waitFor({ state: 'visible', timeout: 10_000 });
        await page.screenshot({ path: shot('share-file-tab-modal-open') });
        expect(await shareModal.locator(':has-text("Share file")').count()).toBeGreaterThan(0);
        await page.keyboard.press('Escape');
        await shareModal.waitFor({ state: 'hidden', timeout: 10_000 });
    }, 120_000);
});
