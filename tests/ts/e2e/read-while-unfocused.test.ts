/**
 * E2E test: messages that arrive while the window is visible but unfocused stay unread (#5408).
 *
 * Alice keeps her peer chat with Bob open at its tail while he posts. With her window focused his message
 * is read at once; with it unfocused it stays unread, even while the pointer moves over the page, and
 * focusing the window marks it read. Losing the focus is simulated in the page - document.hasFocus()
 * and the window's blur/focus events - because a headless browser has no window to switch away from.
 *
 * Bob is a third test account, so no other spec opens this chat: a closed page's Blazor circuit lives on
 * for a minute (DisconnectedCircuitRetentionPeriod) and keeps reading whatever chat it was showing.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/read-while-unfocused.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { BrowserContext, Page } from 'playwright';
import {
    BASE_URL, TEST_EMAIL, connectBrowser, newUserContext, openChat, screenshot, withUILanguage,
    type BrowserConnection,
} from './helpers';
import { getUserId } from './peer-call';

const TEST_EMAIL_3 = 'test-claude-agent-3@actual.chat';
const shot = (name: string) => screenshot('e2e-read-while-unfocused', name);

async function typeAndSend(page: Page, text: string) {
    const messageInput = page.locator('#message-input .editor-content[contenteditable="true"]').first();
    await messageInput.click({ force: true });
    await messageInput.evaluate(el => {
        el.innerHTML = '';
        el.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await page.keyboard.type(text);
    await messageInput.dispatchEvent('keypress', {
        key: 'Enter', code: 'Enter', bubbles: true, cancelable: true,
    });
    await page.locator(`.chat-message-markup:has-text("${text}")`).first()
        .waitFor({ state: 'visible', timeout: 15_000 });
}

// Playwright emulates a focused page, so document.hasFocus() is overridden on the document itself
async function setWindowFocus(page: Page, isFocused: boolean) {
    await page.evaluate(isFocused => {
        if (isFocused)
            Reflect.deleteProperty(document, 'hasFocus');
        else
            Object.defineProperty(document, 'hasFocus', { value: () => false, configurable: true });
        window.dispatchEvent(new Event(isFocused ? 'focus' : 'blur'));
    }, isFocused);
}

// Navigates within the page's own circuit, so the circuit left behind by closing it doesn't show the chat
async function leaveChat(page: Page) {
    await page.evaluate(() => (window as unknown as { debugUI: { navigateTo(url: string): void } })
        .debugUI.navigateTo('/chat/announcements'))
        .catch(() => { /* ignore */ });
    await page.waitForTimeout(1_000);
}

describe('reading while the window is unfocused', () => {
    let conn: BrowserConnection;
    let aliceCtx: BrowserContext;
    let bobCtx: BrowserContext;
    let alice: Page;
    let bob: Page;

    beforeAll(async () => {
        conn = await connectBrowser();
        ({ context: aliceCtx, page: alice } = await newUserContext(conn, TEST_EMAIL));
        ({ context: bobCtx, page: bob } = await newUserContext(conn, TEST_EMAIL_3));
    }, 180_000);

    afterAll(async () => {
        await leaveChat(alice);
        await leaveChat(bob);
        await aliceCtx.close().catch(() => { /* ignore */ });
        await bobCtx.close().catch(() => { /* ignore */ });
        if (conn.ownsBrowser) {
            await conn.context.close().catch(() => { /* ignore */ });
            await conn.browser.close().catch(() => { /* ignore */ });
        }
    });

    it('keeps messages unread until the window is focused', async () => {
        // arrange - Alice speaks first, so Bob, not her contact, may post more than once
        const stamp = Date.now();
        const chatId = `p-${[await getUserId(alice), await getUserId(bob)].sort().join('-')}`;
        const chatUrl = withUILanguage(`${BASE_URL}/chat/${chatId}`);
        await openChat(alice, chatUrl);
        await typeAndSend(alice, `unfocused-read-hi-${stamp}`);
        await openChat(bob, chatUrl);
        // MenuRef base64-encodes its arguments
        const item = alice.locator(`.navbar-item[data-menu*="|${btoa(chatId)}|"]`).first();
        await item.waitFor({ state: 'visible', timeout: 15_000 });
        const badge = item.locator('.message-counter-badge:visible');
        // Reactions and mentions from earlier runs aren't cleared by reading, only by "Mark as read"
        if (await badge.count() > 0) {
            await item.click({ button: 'right' });
            await alice.locator('.ac-menu-host .ac-menu li.ac-menu-item:has-text("Mark as read")').first().click();
        }
        await expect.poll(() => badge.count(), { timeout: 15_000 }).toBe(0);

        // act - a message while Alice's window is focused
        const focusedText = `unfocused-read-focused-${stamp}`;
        await typeAndSend(bob, focusedText);
        await alice.locator(`.chat-message-markup:has-text("${focusedText}")`).first()
            .waitFor({ state: 'visible', timeout: 15_000 });
        await alice.waitForTimeout(3_000);

        // assert
        expect(await badge.count()).toBe(0);
        await alice.screenshot({ path: shot('1-focused-read') });

        // act - a message while it's unfocused
        await setWindowFocus(alice, false);
        const unfocusedText = `unfocused-read-unfocused-${stamp}`;
        await typeAndSend(bob, unfocusedText);
        await alice.locator(`.chat-message-markup:has-text("${unfocusedText}")`).first()
            .waitFor({ state: 'visible', timeout: 15_000 });

        // assert
        await expect.poll(() => badge.first().textContent().catch(() => ''), { timeout: 15_000 }).toBe('1');
        await alice.screenshot({ path: shot('2-unfocused-unread') });

        // act - the pointer moves over the unfocused page for longer than UserActivityUI's 10s report throttle
        for (let i = 0; i < 7; i++) {
            await alice.evaluate(i => document.dispatchEvent(
                new PointerEvent('pointermove', { clientX: 600 + 10 * i, clientY: 400 })), i);
            await alice.waitForTimeout(2_000);
        }

        // assert
        expect(await badge.first().textContent()).toBe('1');

        // act - the window gets the focus back
        await setWindowFocus(alice, true);

        // assert
        await expect.poll(() => badge.count(), { timeout: 15_000 }).toBe(0);
        await alice.screenshot({ path: shot('3-refocused-read') });
    }, 180_000);
});
