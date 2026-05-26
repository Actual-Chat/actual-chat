/**
 * E2E test: Verify the Share button in the visual media viewer sends the
 * selected media as a new message via the chat picker modal.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/share-media-button.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, beforeAll, afterAll } from 'vitest';
import type { Page } from 'playwright';
import * as path from 'path';
import {
    BASE_URL, connectBrowser, ensureSignedIn, skipOnboarding, screenshot,
    waitForChatReady, waitForEditor, type BrowserConnection,
} from './helpers';

// Real 32x32 JPEG from the .NET test fixtures — small but valid enough that
// the server-side ImageUploadProcessor classifies it as visual media.
const TEST_IMAGE_PATH = path.resolve(
    process.cwd(),
    'tests/Testing/TestImages/default.jpg',
);

const shot = (name: string) => screenshot('e2e', name);

const CHAT_URL = `${BASE_URL}/chat/the-actual-one`;

describe('share media as a new message', () => {
    let conn: BrowserConnection;
    let page: Page;
    const tag = `share-${Date.now().toString(36)}`;

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

    it('sends a media-only message from the visual viewer to Notes', async () => {
        // arrange — open a chat we can post in
        await page.goto(CHAT_URL, { waitUntil: 'domcontentloaded' });
        await waitForChatReady(page);
        await skipOnboarding(page);

        const joinButton = page.locator('button:has-text("Join this chat")');
        if (await joinButton.isVisible({ timeout: 3000 }).catch(() => false)) {
            await joinButton.click();
            await page.waitForTimeout(1500);
        }

        await waitForEditor(page);

        // act — attach an image and post a message with a unique marker tag
        const fileInput = page.locator('input.attachment-web-file-picker').first();
        await fileInput.waitFor({ state: 'attached', timeout: 5_000 });
        await fileInput.setInputFiles(TEST_IMAGE_PATH);

        // The attachment-list-wrapper renders only when AttachmentList has items;
        // wait for it so we know the upload was accepted before posting.
        await page.locator('.attachment-list-wrapper').first()
            .waitFor({ state: 'visible', timeout: 15_000 });

        const messageInput = page.locator('#message-input .editor-content[contenteditable="true"]').first();
        await messageInput.click({ force: true });
        // Clear any stale draft (ChatMessageEditor persists per-chat drafts).
        await messageInput.evaluate(el => {
            el.innerHTML = '';
            el.dispatchEvent(new Event('input', { bubbles: true }));
        });
        await messageInput.click({ force: true });
        await page.keyboard.type(tag);
        await page.screenshot({ path: shot('share-before-post') });

        await messageInput.dispatchEvent('keypress', {
            key: 'Enter', code: 'Enter', bubbles: true, cancelable: true,
        });

        // The chat-view unmounts briefly after a post; re-navigate to re-render it.
        await page.waitForTimeout(2_000);
        await page.goto(CHAT_URL, { waitUntil: 'domcontentloaded' });
        await waitForChatReady(page);
        await skipOnboarding(page);

        const postedEntry = page
            .locator(`[data-chat-entry-id]:has(.chat-message-markup:has-text("${tag}"))`)
            .first();
        await postedEntry.waitFor({ state: 'visible', timeout: 30_000 });
        await page.screenshot({ path: shot('share-posted') });

        // act — open the visual media viewer by clicking the attached image
        const attachmentImage = postedEntry.locator('.image-attachment .c-content').first();
        await attachmentImage.waitFor({ state: 'visible', timeout: 15_000 });
        await attachmentImage.click();

        const viewerHeader = page.locator('.image-viewer-header').first();
        await viewerHeader.waitFor({ state: 'visible', timeout: 10_000 });
        await page.screenshot({ path: shot('share-viewer-open') });

        // act — click the Share button in the viewer header
        const shareBtn = viewerHeader.locator('button:has(i.icon-share)').first();
        await shareBtn.waitFor({ state: 'visible', timeout: 5_000 });
        await shareBtn.click();

        // assert — ForwardMessageModal appears
        const forwardModal = page.locator('.forward-message-modal').first();
        await forwardModal.waitFor({ state: 'visible', timeout: 10_000 });
        await page.screenshot({ path: shot('share-forward-open') });

        // act — pick the Notes chat (NotesFirst ordering pins it at the top)
        const firstContact = forwardModal.locator('.contact-selector-list-item').first();
        await firstContact.waitFor({ state: 'visible', timeout: 10_000 });
        await firstContact.click();
        await page.waitForTimeout(300);

        // act — submit
        const sendBtn = forwardModal.locator('button:has-text("Share"):not([disabled])').first();
        await sendBtn.waitFor({ state: 'visible', timeout: 5_000 });
        await sendBtn.click();

        // assert — modal closes after sharing
        await forwardModal.waitFor({ state: 'hidden', timeout: 15_000 });
        await page.screenshot({ path: shot('share-forward-closed') });

        // assert — success toast appears confirming the share
        const toast = page.locator('.toast-container .toast:has-text("Shared media")').first();
        await toast.waitFor({ state: 'visible', timeout: 10_000 });
        await page.screenshot({ path: shot('share-toast') });
    }, 180_000);
});
