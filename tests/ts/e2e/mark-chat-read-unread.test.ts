/**
 * E2E test: "Mark as read" / "Mark as unread" in a chat list item's context menu.
 *
 * Alice watches the default chat from another chat while Bob reacts to her message, mentions her
 * and posts. Each badge is cleared by "Mark as read", including the reaction one, which the read
 * position alone doesn't clear. Then "Mark as unread" shows a "1" badge that survives a reload and
 * is cleared by opening the chat; the open chat itself keeps the mark until it's left and reopened.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/mark-chat-read-unread.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { BrowserContext, Locator, Page } from 'playwright';
import {
    BASE_URL, TEST_EMAIL, TEST_EMAIL_2, connectBrowser, newUserContext, openChat, screenshot,
    withUILanguage, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e-mark-read-unread', name);
const OTHER_CHAT_URL = withUILanguage(`${BASE_URL}/chat/announcements`);

async function typeAndSend(page: Page, text: string, mustClear = true) {
    const messageInput = page.locator('#message-input .editor-content[contenteditable="true"]').first();
    if (mustClear) {
        await messageInput.click({ force: true });
        await messageInput.evaluate(el => {
            el.innerHTML = '';
            el.dispatchEvent(new Event('input', { bubbles: true }));
        });
    }
    await page.keyboard.type(text);
    await messageInput.dispatchEvent('keypress', {
        key: 'Enter', code: 'Enter', bubbles: true, cancelable: true,
    });
    await page.locator(`.chat-message-markup:has-text("${text.trim()}")`).first()
        .waitFor({ state: 'visible', timeout: 15_000 });
}

async function mention(page: Page, name: string, text: string) {
    const messageInput = page.locator('#message-input .editor-content[contenteditable="true"]').first();
    await messageInput.click({ force: true });
    await messageInput.evaluate(el => {
        el.innerHTML = '';
        el.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await page.keyboard.type(`@${name.split(' ')[0]}`);
    await page.locator(`.mention-list:not(.non-visible) .mention-list-item.selected:has-text("${name}")`).first()
        .waitFor({ state: 'visible', timeout: 10_000 });
    await page.keyboard.press('Enter');
    // Text typed before the mention token lands goes in ahead of it, and the mention is lost
    await messageInput.locator('.editor-mention').first().waitFor({ state: 'attached', timeout: 5_000 });
    await typeAndSend(page, ` ${text}`, false);
}

async function entryLidOf(page: Page, text: string): Promise<number> {
    const entryId = await page.locator(`[data-chat-entry-id]:has(.chat-message-markup:has-text("${text}"))`)
        .first()
        .getAttribute('data-chat-entry-id');
    expect(entryId).toBeTruthy();
    return Number(entryId!.split(':').pop());
}

// The hover menu's first button reacts with a thumbs-up right away.
async function reactTo(page: Page, entryLid: number) {
    const message = page.locator(`[data-chat-entry-id$=":${entryLid}"] .chat-message`).first();
    await message.waitFor({ state: 'visible', timeout: 15_000 });
    await message.hover();
    const reactButton = page.locator('.message-hover-menu-new .btn-round').first();
    await reactButton.waitFor({ state: 'visible', timeout: 5_000 });
    await reactButton.click();
    await page.locator(`[data-chat-entry-id$=":${entryLid}"] .message-reactions`).first()
        .waitFor({ state: 'visible', timeout: 15_000 });
}

async function openMenu(page: Page, item: Locator, text: string): Promise<Locator> {
    await item.click({ button: 'right' });
    const entry = page.locator(`.ac-menu-host .ac-menu li.ac-menu-item:has-text("${text}")`).first();
    await entry.waitFor({ state: 'visible', timeout: 10_000 });
    // The menu fades in, and a screenshot taken mid-fade barely shows it
    await page.evaluate(() => Promise.all(document.getAnimations()
        .filter(a => a.effect?.getTiming().iterations !== Infinity)
        .map(a => a.finished.catch(() => undefined))));
    return entry;
}

describe('mark chat as read / unread', () => {
    let conn: BrowserConnection;
    let aliceCtx: BrowserContext;
    let bobCtx: BrowserContext;
    let alice: Page;
    let bob: Page;

    beforeAll(async () => {
        conn = await connectBrowser();
        ({ context: aliceCtx, page: alice } = await newUserContext(conn, TEST_EMAIL));
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

    it('clears reaction, mention and message badges, and marks a read chat unread', async () => {
        // arrange - Alice's message follows Bob's, so Bob's view shows her name above it
        const stamp = Date.now();
        const aliceText = `mark-alice-${stamp}`;
        await openChat(bob);
        await typeAndSend(bob, `mark-bob-${stamp}`);
        await openChat(alice);
        await typeAndSend(alice, aliceText);
        await bob.locator(`.chat-message-markup:has-text("${aliceText}")`).first()
            .waitFor({ state: 'visible', timeout: 15_000 });
        const aliceName = (await bob.locator('.chat-message-author-name').last().innerText()).trim();
        expect(aliceName).toBeTruthy();
        const aliceLid = await entryLidOf(bob, aliceText);

        await openChat(alice, OTHER_CHAT_URL);
        // MenuRef base64-encodes its arguments
        const item = alice.locator(`.navbar-item[data-menu*="|${btoa('the-actual-one')}|"]`).first();
        await item.waitFor({ state: 'visible', timeout: 15_000 });
        const badge = item.locator('.message-counter-badge:visible');
        const tabBadges = alice.locator('.c-badge[data-child="badge"] .message-counter-badge:visible');
        const navbarBadge = alice.locator('.navbar-button:has(.icon-message-ellipse) .badge:visible');
        const expectAllRead = async () => {
            await expect.poll(() => badge.count(), { timeout: 15_000 }).toBe(0);
            await expect.poll(() => tabBadges.count(), { timeout: 15_000 }).toBe(0);
            await expect.poll(() => navbarBadge.count(), { timeout: 15_000 }).toBe(0);
        };
        // Reactions from an earlier failed run outlive it
        if (await badge.count() > 0)
            await (await openMenu(alice, item, 'Mark as read')).click();
        await expectAllRead();

        // act - a reaction to Alice's message
        await reactTo(bob, aliceLid);

        // assert
        await item.locator('.message-counter-badge.unread-reaction').first()
            .waitFor({ state: 'visible', timeout: 30_000 });
        let entry = await openMenu(alice, item, 'Mark as read');
        await alice.screenshot({ path: shot('1-reaction-menu') });
        await entry.click();
        await expectAllRead();
        await alice.screenshot({ path: shot('1-reaction-cleared') });

        // act - a mention of Alice
        await mention(bob, aliceName, `mark-ping-${stamp}`);

        // assert
        await item.locator('.message-counter-badge.unread-mention').first()
            .waitFor({ state: 'visible', timeout: 30_000 });
        entry = await openMenu(alice, item, 'Mark as read');
        await alice.screenshot({ path: shot('2-mention-menu') });
        await entry.click();
        await expectAllRead();
        await alice.screenshot({ path: shot('2-mention-cleared') });

        // act - plain messages
        await typeAndSend(bob, `mark-plain-1-${stamp}`);
        await typeAndSend(bob, `mark-plain-2-${stamp}`);

        // assert
        await expect.poll(() => badge.first().textContent().catch(() => ''), { timeout: 30_000 }).toBe('2');
        entry = await openMenu(alice, item, 'Mark as read');
        await alice.screenshot({ path: shot('3-messages-menu') });
        await entry.click();
        await expectAllRead();
        await alice.screenshot({ path: shot('3-messages-cleared') });

        // act - mark the read chat unread
        entry = await openMenu(alice, item, 'Mark as unread');
        await alice.screenshot({ path: shot('4-unread-menu') });
        await entry.click();

        // assert
        await expect.poll(() => badge.first().textContent().catch(() => ''), { timeout: 15_000 }).toBe('1');
        await alice.screenshot({ path: shot('4-marked-unread') });
        await alice.reload();
        await item.waitFor({ state: 'visible', timeout: 30_000 });
        await expect.poll(() => badge.first().textContent().catch(() => ''), { timeout: 15_000 }).toBe('1');

        // act - opening the chat clears the mark
        await openChat(alice);
        await openChat(alice, OTHER_CHAT_URL);

        // assert
        await item.waitFor({ state: 'visible', timeout: 15_000 });
        await expectAllRead();

        // act - the open chat can be marked unread too, and keeps the mark while it stays open
        await openChat(alice);
        await item.waitFor({ state: 'visible', timeout: 15_000 });
        await (await openMenu(alice, item, 'Mark as unread')).click();
        await alice.waitForTimeout(2_000);
        entry = await openMenu(alice, item, 'Mark as read');
        await alice.screenshot({ path: shot('5-open-chat-marked-unread-menu') });
        await alice.keyboard.press('Escape');
        await openChat(alice, OTHER_CHAT_URL);

        // assert
        await item.waitFor({ state: 'visible', timeout: 15_000 });
        await expect.poll(() => badge.first().textContent().catch(() => ''), { timeout: 15_000 }).toBe('1');
        await (await openMenu(alice, item, 'Mark as read')).click();
        await expectAllRead();
    }, 300_000);
});
