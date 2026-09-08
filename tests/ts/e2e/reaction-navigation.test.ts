/**
 * E2E test: the "navigate to unread reaction" button in the chat view.
 *
 * Alice posts a target message and enough filler to scroll it out of view; Bob reacts to
 * the target and to one filler message. Alice reopens the chat at its tail and sees the
 * button with a "2" badge; each click jumps to the closest reacted message and, once it's
 * on screen, the reaction is dismissed and the badge counts down until the button hides.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/reaction-navigation.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { BrowserContext, Page } from 'playwright';
import {
    BASE_URL, TEST_EMAIL, TEST_EMAIL_2, connectBrowser, newUserContext, screenshot,
    skipOnboarding, waitForChatReady, waitForEditor, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e-reaction-nav', name);

const CHAT_URL = `${BASE_URL}/chat/the-actual-one`;
// A 720px viewport shows ~22 one-line messages: the two reacted messages must sit more than
// a screen apart from each other and from the tail, or one of them is on screen at the moment
// it's expected to be a jump target - and gets dismissed as seen instead.
const FILLER_COUNT = 60;
const SECOND_INDEX = 28;

async function openChat(page: Page, url: string = CHAT_URL) {
    await page.goto(url, { waitUntil: 'domcontentloaded' });
    await waitForChatReady(page);
    await skipOnboarding(page);

    const joinButton = page.locator('button:has-text("Join this chat")');
    if (await joinButton.isVisible({ timeout: 3000 }).catch(() => false)) {
        await joinButton.click();
        await page.waitForTimeout(1500);
    }
    await waitForEditor(page);
}

async function post(page: Page, text: string) {
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

async function waitInViewport(page: Page, entryLid: number) {
    await expect.poll(() => page.locator(`[data-chat-entry-id$=":${entryLid}"]`).first().evaluate(el => {
        const r = el.getBoundingClientRect();
        return r.top >= 0 && r.bottom <= window.innerHeight;
    }).catch(() => false), { timeout: 15_000 }).toBe(true);
}

// Reaction notifications outlive a failed run, so each run starts by clicking through
// whatever is left - every click dismisses one.
async function drainReactions(page: Page) {
    const button = page.locator('.navigate-to-reaction .btn-round').first();
    for (let i = 0; i < 20; i++) {
        if (!await button.isVisible({ timeout: 3_000 }).catch(() => false))
            return;

        await button.click({ force: true }).catch(() => { /* ignore */ });
        await page.waitForTimeout(1_000);
    }
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

describe('navigate to unread reaction', () => {
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

    it('jumps to each reacted message in turn and counts the badge down', async () => {
        // arrange — Alice posts a target and fillers, Bob reacts to the target and a filler
        const stamp = Date.now();
        const targetText = `rx-target-${stamp}`;
        await openChat(alice);
        await drainReactions(alice);
        await post(alice, targetText);
        const targetLid = await entryLidOf(alice, targetText);
        let secondText = '';
        for (let i = 0; i < FILLER_COUNT; i++) {
            const text = `rx-filler-${stamp}-${i}`;
            await post(alice, text);
            if (i === SECOND_INDEX)
                secondText = text;
        }
        const secondLid = await entryLidOf(alice, secondText);

        await openChat(bob, `${CHAT_URL}?n=${targetLid}`);
        await reactTo(bob, targetLid);
        await openChat(bob, `${CHAT_URL}?n=${secondLid}`);
        await reactTo(bob, secondLid);
        await bob.screenshot({ path: shot('bob-reacted') });

        // act — Alice reopens the chat at its tail
        await openChat(alice);
        const button = alice.locator('.navigate-to-reaction .btn-round').first();
        const badge = alice.locator('.navigate-to-reaction .unread-counter').first();
        await button.waitFor({ state: 'visible', timeout: 30_000 });
        await expect.poll(() => badge.textContent(), { timeout: 15_000 }).toMatch(/2/);
        await alice.screenshot({ path: shot('alice-badge-2') });

        // assert — the first click lands on the closest reacted message (the filler), the
        // reaction gets dismissed, and the badge drops to 1
        await button.click();
        await waitInViewport(alice, secondLid);
        await expect.poll(() => badge.textContent().catch(() => ''), { timeout: 15_000 }).toMatch(/1/);
        await alice.waitForTimeout(1000);
        await alice.screenshot({ path: shot('alice-after-first-click') });

        // the second click lands on the target, and the button goes away
        await button.click();
        await waitInViewport(alice, targetLid);
        await alice.locator('.navigate-to-reaction').first()
            .waitFor({ state: 'hidden', timeout: 15_000 });
        await alice.screenshot({ path: shot('alice-after-second-click') });
    }, 300_000);
});
