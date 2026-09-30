/**
 * E2E test: forwarding a message with an attachment into a peer chat with a user who hasn't
 * added the sender to their contacts (#4964).
 *
 * A fresh account is a non-contact for Bob by construction, and non-contacts can't send files.
 * It posts a text message and a file message into the shared chat and opens the peer chat with
 * Bob: the forward picker must list Bob for the text message and leave him out for the file one.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/forward-attachments-non-contact.test.ts \
 *     --config vitest.config.e2e.ts
 */

import { describe, it, beforeAll, afterAll, expect } from 'vitest';
import type { BrowserContext, Locator, Page } from 'playwright';
import {
    BASE_URL, DEFAULT_CHAT_URL, TEST_EMAIL_2, connectBrowser, joinChat, newUserContext, openChat,
    screenshot, waitForChatReady, withUILanguage, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e-forward-non-contact', name);

async function getUserId(page: Page): Promise<string> {
    return await page.evaluate(() => (window as unknown as { debugUI: { getUserId(): Promise<string> } })
        .debugUI.getUserId());
}

async function typeInEditor(page: Page, text: string): Promise<Locator> {
    const editor = page.locator('#message-input .editor-content[contenteditable="true"]').first();
    await editor.waitFor({ state: 'visible', timeout: 30_000 });
    for (let attempt = 0; attempt < 3; attempt++) {
        await editor.click({ force: true });
        await editor.evaluate(el => {
            el.innerHTML = '';
            el.dispatchEvent(new Event('input', { bubbles: true }));
        });
        await editor.click({ force: true });
        await page.keyboard.type(text, { delay: 20 });
        if ((await editor.innerText()).trim() === text)
            return editor;

        await page.waitForTimeout(1_000);
    }
    throw new Error(`typeInEditor: "${text}" never landed in the editor`);
}

async function openMessageMenu(page: Page, entry: Locator): Promise<void> {
    await entry.locator('.chat-message-markup').first().evaluate(el => {
        const rect = el.getBoundingClientRect();
        const at = { bubbles: true, cancelable: true, button: 2, clientX: rect.left + 10, clientY: rect.top + 10 };
        el.dispatchEvent(new PointerEvent('pointerdown', at));
        el.dispatchEvent(new MouseEvent('mousedown', at));
        el.dispatchEvent(new MouseEvent('contextmenu', at));
    });
}

async function openForwardModal(page: Page, entry: Locator, search: string): Promise<Locator> {
    await entry.waitFor({ state: 'visible', timeout: 30_000 });
    await openMessageMenu(page, entry);
    const forwardItem = page.locator('li.ac-menu-item:has-text("Forward")').first();
    await forwardItem.waitFor({ state: 'visible', timeout: 10_000 });
    await forwardItem.click();
    const modal = page.locator('.forward-message-modal').first();
    await modal.waitFor({ state: 'visible', timeout: 10_000 });
    await modal.locator('input').first().fill(search);
    await page.waitForTimeout(1_500);
    return modal;
}

describe('forward attachments to a non-contact peer chat', () => {
    let conn: BrowserConnection;
    let aliceCtx: BrowserContext;
    let bobCtx: BrowserContext;
    let alice: Page;
    let bob: Page;
    const tag = `fwd-${Date.now().toString(36)}`;
    const aliceEmail = `test-claude-agent-${tag}@actual.chat`;

    beforeAll(async () => {
        conn = await connectBrowser();
        // Sequential sign-ins: parallel ones race on the shared server flow (see vitest.config.e2e.ts).
        ({ context: aliceCtx, page: alice } = await newUserContext(conn, aliceEmail));
        ({ context: bobCtx, page: bob } = await newUserContext(conn, TEST_EMAIL_2));
    }, 180_000);

    afterAll(async () => {
        await aliceCtx.close().catch(() => { /* ignore */ });
        await bobCtx.close().catch(() => { /* ignore */ });
        if (conn.ownsBrowser) {
            await conn.context.close().catch(() => { /* ignore */ });
            await conn.browser.close().catch(() => { /* ignore */ });
        }
    });

    it('leaves the non-contact peer out of the picker when the message has files', async () => {
        // arrange - Alice posts a text message and a file message into the shared chat
        await openChat(alice);
        await joinChat(alice);
        await typeInEditor(alice, `text ${tag}`);
        await alice.keyboard.press('Enter');
        const textSource = alice
            .locator(`[data-chat-entry-id]:has(.chat-message-markup:has-text("text ${tag}"))`)
            .first();
        await textSource.waitFor({ state: 'visible', timeout: 30_000 });
        const fileInput = alice.locator('input.attachment-web-file-picker').first();
        await fileInput.waitFor({ state: 'attached', timeout: 15_000 });
        await fileInput.setInputFiles({
            name: `${tag}.txt`,
            mimeType: 'text/plain',
            buffer: Buffer.from(`forwarded file ${tag}\n`),
        });
        await alice.locator('.attachment-list-wrapper').first().waitFor({ state: 'visible', timeout: 15_000 });
        await alice.waitForTimeout(2_000);
        await typeInEditor(alice, tag);
        await alice.keyboard.press('Enter');
        const fileSource = alice
            .locator(`[data-chat-entry-id]:has(.file-attachment):has(.chat-message-markup:has-text("${tag}"))`)
            .first();
        await fileSource.waitFor({ state: 'visible', timeout: 30_000 });
        await alice.screenshot({ path: shot('1-alice-sources-posted') });

        // arrange - Alice opens the peer chat with Bob (not a contact of his) and says hi
        const userIds = [await getUserId(alice), await getUserId(bob)].sort();
        const peerChatUrl = withUILanguage(`${BASE_URL}/chat/p-${userIds.join('-')}`);
        await alice.goto(peerChatUrl, { waitUntil: 'domcontentloaded' });
        await waitForChatReady(alice);
        await typeInEditor(alice, `hi ${tag}`);
        await alice.keyboard.press('Enter');
        await alice.locator(`.chat-message-markup:has-text("hi ${tag}")`).first()
            .waitFor({ state: 'visible', timeout: 20_000 });
        const bobName = (await alice.locator('.chat-header-title .c-title').first().innerText()).trim();
        expect(bobName).not.toBe('');
        await alice.goto(withUILanguage(DEFAULT_CHAT_URL), { waitUntil: 'domcontentloaded' });
        await waitForChatReady(alice);
        const bobItemSelector = `.contact-selector-list-item:has-text("${bobName}")`;

        // act - forward the text message
        let modal = await openForwardModal(alice, textSource, bobName);

        // assert - Bob is a valid target for text
        await modal.locator(bobItemSelector).first().waitFor({ state: 'visible', timeout: 15_000 });
        await alice.screenshot({ path: shot('2-alice-forward-text') });
        await alice.keyboard.press('Escape');
        await modal.waitFor({ state: 'hidden', timeout: 10_000 });

        // act - forward the file message
        modal = await openForwardModal(alice, fileSource, bobName);

        // assert - Bob isn't offered, so the forward can't even be attempted
        await alice.screenshot({ path: shot('3-alice-forward-file') });
        expect(await modal.locator(bobItemSelector).count()).toBe(0);
        await alice.keyboard.press('Escape');
    }, 240_000);
});
