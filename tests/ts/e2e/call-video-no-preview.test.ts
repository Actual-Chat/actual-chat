/**
 * E2E test: turning the camera on mid-call starts video at once, full-screen, without the join
 * preview (#4932).
 *
 * Two users at a phone-sized viewport (the full-screen call view exists only on the narrow
 * layout) are on a call; the caller taps the camera on the call screen. The join preview must
 * not show, the caller's own video must be streaming in the expanded video panel, and the call
 * screen must stay up until that panel covers the screen - the chat never shows in between.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch), locally: calls are incomplete UI, which the test
 *   turns on for both accounts - test agents are admins only on a local server.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/call-video-no-preview.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import type { BrowserContext, Page } from 'playwright';
import {
    BASE_URL, TEST_EMAIL, TEST_EMAIL_2, connectBrowser, ensureSignedIn, screenshot, setIncompleteUI,
    skipOnboarding, withUILanguage,
    type BrowserConnection,
} from './helpers';
import { SPEECH_WAV } from './video-call';

const shot = (name: string) => screenshot('e2e-call-video', name);

const CALL_SCREEN = '.full-screen-call-view.in-call';
const JOIN_PREVIEW = '.modal .camera-preview-video';

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
    });
    // The fake-device flags answer getUserMedia, but the app reads the Permissions API first.
    await context.grantPermissions(['microphone', 'camera'], { origin: BASE_URL });
    const page = await context.newPage();
    page.on('pageerror', e => console.log(`PAGEERROR[${email}]:`, e.message));
    await ensureSignedIn(page, email);
    return { context, page };
}

async function send(page: Page, text: string) {
    // Focused, not clicked: the record button overlaps the editor on the narrow layout.
    const editor = page.locator('#message-input .editor-content[contenteditable="true"]').first();
    await editor.waitFor({ state: 'visible', timeout: 30_000 });
    await editor.focus();
    await page.keyboard.type(text, { delay: 20 });
    await page.keyboard.press('Enter');
    await page.waitForTimeout(1_500);
}

async function getUserId(page: Page): Promise<string> {
    return await page.evaluate(() => (window as unknown as { debugUI: { getUserId(): Promise<string> } })
        .debugUI.getUserId());
}

async function hangUpIfAny(page: Page | undefined) {
    if (!page)
        return;

    const hangUps = [
        page.locator(`${CALL_SCREEN} .c-call-bar .btn-video-panel.talking`).first(),
        page.locator('.video-panel .btn-video-panel.talking').first(),
    ];
    for (const hangUp of hangUps) {
        if (await hangUp.isVisible().catch(() => false))
            await hangUp.click().catch(() => { /* ignore */ });
    }
}

/** Expanded and opaque: `isVisible` alone is true for a panel that is still fading in over the chat. */
function isVideoCoveringScreen(page: Page): Promise<boolean> {
    return page.evaluate(() => {
        const panel = document.querySelector('.video-panel.expanded');
        const content = panel?.querySelector('.video-panel-content');
        return !!panel && !!content && getComputedStyle(panel).opacity === '1'
            && getComputedStyle(content).opacity === '1';
    }).catch(() => false);
}

describe('camera on during a call', () => {
    let conn: BrowserConnection;
    let callerCtx: BrowserContext;
    let calleeCtx: BrowserContext;
    let caller: Page;
    let callee: Page;

    beforeAll(async () => {
        // Real speech, not the fake mic's beep: a silent recording idles out, and the call with it.
        conn = await connectBrowser({ fakeAudioFile: SPEECH_WAV });
        // Sequential sign-ins: parallel ones race on the shared server flow (see vitest.config.e2e.ts).
        ({ context: callerCtx, page: caller } = await newPhoneContext(conn, TEST_EMAIL));
        ({ context: calleeCtx, page: callee } = await newPhoneContext(conn, TEST_EMAIL_2));
        for (const page of [caller, callee]) {
            await skipOnboarding(page);
            await setIncompleteUI(page, true);
        }
    }, 180_000);

    afterEach(async () => {
        await hangUpIfAny(caller);
        await hangUpIfAny(callee);
    }, 30_000);

    afterAll(async () => {
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

    it('starts video at once, full-screen, without the join preview', async () => {
        // arrange - a reply from the callee opens the peer-call gate; then dial and accept
        const userIds = [await getUserId(caller), await getUserId(callee)].sort();
        const pm = withUILanguage(`${BASE_URL}/chat/p-${userIds.join('-')}`);
        await caller.goto(pm, { waitUntil: 'domcontentloaded' });
        await send(caller, 'video call test');
        await callee.goto(pm, { waitUntil: 'domcontentloaded' });
        await send(callee, 'video call test reply');
        await caller.reload({ waitUntil: 'domcontentloaded' });
        const callButton = caller.locator('.btn-start-call').first();
        await callButton.waitFor({ state: 'visible', timeout: 30_000 });
        await callButton.click();
        const accept = callee.locator('.btn-call.c-accept').first();
        await accept.waitFor({ state: 'visible', timeout: 30_000 });
        await accept.click();
        for (const page of [caller, callee])
            await page.locator(`${CALL_SCREEN} .c-toolbar`).first().waitFor({ state: 'visible', timeout: 30_000 });

        // act - the caller turns the camera on from the call screen
        let hasSeenJoinPreview = false;
        let hasSeenChat = false;
        await caller.locator(`${CALL_SCREEN} .c-toolbar .btn-video-toggle`).first().click();
        const callScreen = caller.locator(CALL_SCREEN).first();
        await expect.poll(async () => {
            hasSeenJoinPreview ||= await caller.locator(JOIN_PREVIEW).first().isVisible().catch(() => false);
            const isCovered = await isVideoCoveringScreen(caller);
            // Neither the call screen nor the video covering the screen means the chat showed in between
            hasSeenChat ||= !isCovered && !await callScreen.isVisible().catch(() => false);
            return isCovered;
        }, { timeout: 30_000, interval: 50 }).toBe(true);

        // assert - the camera is on, full-screen, with nothing asked in between; the other side gets the video
        expect(hasSeenJoinPreview, 'mid-call the camera starts without the join preview').toBe(false);
        expect(hasSeenChat, 'the call screen stays up until the video covers the screen').toBe(false);
        await callScreen.waitFor({ state: 'hidden', timeout: 10_000 });
        await caller.locator('.video-panel.expanded .video-streaming-preview').first()
            .waitFor({ state: 'visible', timeout: 20_000 });
        await caller.screenshot({ path: shot('caller-video-on') });
        await callee.locator(`${CALL_SCREEN} .c-call-bar .btn-video-panel`).first().click();
        await callee.locator('.video-panel .remote-video-container').first()
            .waitFor({ state: 'visible', timeout: 30_000 });
    }, 180_000);
});
