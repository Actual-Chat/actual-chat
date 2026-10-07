/**
 * E2E test: a chat can still be scrolled to its very end after a call in it ends (#5018).
 *
 * Two users at a phone-sized viewport are on a call in their peer chat. When it ends, the live
 * card of the call's block turns into a regular conversation header with the same key. That
 * header must stay an item the virtual list knows about - otherwise the list's model ends above
 * its content by the header's height, and a touch scroll down bounces back before the end.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch), locally: calls are incomplete UI, which the test
 *   turns on for both accounts - test agents are admins only on a local server.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/chat-scroll-after-call.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import type { BrowserContext, ConsoleMessage, Page } from 'playwright';
import {
    TEST_EMAIL, TEST_EMAIL_2, connectBrowser, ensureSignedIn, screenshot, setIncompleteUI,
    type BrowserConnection,
} from './helpers';
import { dialPeerCall, openPeerChat, send } from './peer-call';
import { SPEECH_WAV } from './video-call';

const shot = (name: string) => screenshot('e2e-chat-scroll-after-call', name);

const CALL_SCREEN = '.call-screen.expanded';
const CHAT_LIST = '.chat-view.virtual-list';
/** Taller than the phone viewport, so the chat is longer than the screen whatever it held before */
const TALL_MESSAGE_LINE_COUNT = 30;

async function newPhoneContext(
    conn: BrowserConnection,
    email: string,
): Promise<{ context: BrowserContext; page: Page }> {
    // No touch: the ring buttons attach a swipe-to-answer controller on touch devices.
    const context = await conn.browser.newContext({
        ignoreHTTPSErrors: true,
        locale: 'en-US',
        viewport: { width: 390, height: 844 },
        deviceScaleFactor: 2,
        isMobile: true,
        hasTouch: false,
        permissions: ['microphone'],
    });
    const page = await context.newPage();
    page.on('pageerror', e => console.log(`PAGEERROR[${email}]:`, e.message));
    await ensureSignedIn(page, email);
    return { context, page };
}

async function hangUpIfAny(page: Page | undefined) {
    if (!page)
        return;

    const hangUp = page.locator(`${CALL_SCREEN} .call-screen-header .btn-hang-up`).first();
    if (await hangUp.isVisible({ timeout: 1_000 }).catch(() => false))
        await hangUp.click().catch(() => { /* ignore */ });
}

async function sendTallMessage(page: Page) {
    const editor = page.locator('#message-input .editor-content[contenteditable="true"]').first();
    await editor.focus();
    for (let i = 1; i < TALL_MESSAGE_LINE_COUNT; i++) {
        await page.keyboard.type(`line ${i}`);
        await page.keyboard.press('Shift+Enter');
    }
    await send(page, `line ${TALL_MESSAGE_LINE_COUNT}`);
}

/** Wrappers of the last ended call's block that the virtual list can't see as items or groups. */
async function getUnknownBlockChildren(page: Page): Promise<string[]> {
    return page.evaluate(selector => {
        const footers = document.querySelectorAll(`${selector} .conversation-footer`);
        const block = footers[footers.length - 1].closest('li.group')!;
        return [...block.children]
            .filter(e => !e.matches('.item[data-key], .group'))
            .map(e => e.outerHTML.slice(0, e.outerHTML.indexOf('>') + 1));
    }, CHAT_LIST);
}

/** Runs the virtual list's consistency checker once and returns what it reported: item positions
 *  that differ from the list's model, which is what its scroll limits are computed from. */
async function getModelDrift(page: Page): Promise<string[]> {
    const warnings: string[] = [];
    const onConsole = (message: ConsoleMessage) => {
        if (message.text().includes('model drift'))
            warnings.push(message.text());
    };
    page.on('console', onConsole);
    try {
        await page.evaluate(() => {
            const debugUI = (window as unknown as { debugUI: { virtualListDebug(enable: boolean): void } }).debugUI;
            debugUI.virtualListDebug(true);
            debugUI.virtualListDebug(false);
        });
        await page.waitForTimeout(500);
    }
    finally {
        page.off('console', onConsole);
    }
    return warnings;
}

describe('chat scroll after a call ends', () => {
    let conn: BrowserConnection;
    let callerCtx: BrowserContext;
    let calleeCtx: BrowserContext;
    let caller: Page;
    let callee: Page;

    beforeAll(async () => {
        // Real speech, not the fake mic's beep: its transcript is what the call's block is made of.
        conn = await connectBrowser({ fakeAudioFile: SPEECH_WAV });
        // Sequential sign-ins: parallel ones race on the shared server flow (see vitest.config.e2e.ts).
        ({ context: callerCtx, page: caller } = await newPhoneContext(conn, TEST_EMAIL));
        ({ context: calleeCtx, page: callee } = await newPhoneContext(conn, TEST_EMAIL_2));
        for (const page of [caller, callee])
            await setIncompleteUI(page, true);
    }, 180_000);

    afterEach(async () => {
        await hangUpIfAny(caller);
        await hangUpIfAny(callee);
    }, 30_000);

    afterAll(async () => {
        // Unload before closing: a context closed outright leaves its server-side circuit alive for
        // about a minute, still ringing and heartbeating as this account into the next run.
        for (const page of [caller, callee]) {
            await setIncompleteUI(page, false).catch(() => { /* ignore */ });
            await page.goto('about:blank').catch(() => { /* ignore */ });
        }
        await callerCtx.close().catch(() => { /* ignore */ });
        await calleeCtx.close().catch(() => { /* ignore */ });
        if (conn.ownsBrowser) {
            await conn.context.close().catch(() => { /* ignore */ });
            await conn.browser.close().catch(() => { /* ignore */ });
        }
    });

    it('keeps the list in step with the chat when the call in it ends', async () => {
        // arrange - a call with some transcript in a chat taller than the screen
        await openPeerChat(caller, callee);
        await sendTallMessage(callee);
        await dialPeerCall(caller, callee);
        for (const page of [caller, callee]) {
            await page.locator(`${CALL_SCREEN}.in-call .call-screen-footer`).first()
                .waitFor({ state: 'visible', timeout: 30_000 });
        }
        const list = caller.locator(CHAT_LIST).first();
        const liveHeader = list.locator('.live-conversation-header').first();
        await liveHeader.waitFor({ state: 'attached', timeout: 30_000 });
        await expect.poll(async () => list.locator('li.group.c-last .chat-message-group').count(), { timeout: 30_000 })
            .toBeGreaterThan(0);

        // act - the call ends
        await hangUpIfAny(caller);
        await hangUpIfAny(callee);
        await caller.locator(CALL_SCREEN).first().waitFor({ state: 'hidden', timeout: 30_000 });
        await liveHeader.waitFor({ state: 'detached', timeout: 30_000 });
        await list.locator('.conversation-footer').last().waitFor({ state: 'visible', timeout: 30_000 });
        await caller.waitForTimeout(1_500);
        await caller.screenshot({ path: shot('call-ended') });

        // assert - the block's header is still a list item, so the list's model matches what's on screen
        const unknownBlockChildren = await getUnknownBlockChildren(caller);
        const modelDrift = await getModelDrift(caller);
        expect(unknownBlockChildren, 'every wrapper in the call block is an item or a group').toEqual([]);
        expect(modelDrift, 'no item is off the position the list assumes').toEqual([]);
    }, 180_000);
});
