/**
 * E2E test: turning the camera on mid-call starts video at once, without the join preview (#4932).
 *
 * Two users are on a call, and the caller turns the camera on:
 *   - narrow (phone-sized viewport): from the full-screen call screen, which only the narrow
 *     layout has. Video must open in the expanded video panel, and the call screen must stay up
 *     until that panel covers the screen - the chat never shows in between.
 *   - wide: from the chat's audio panel, where an active call lives on a wide screen. Video must
 *     open in the expanded video panel there too.
 * In both the join preview must not show.
 *
 * Screenshots go to tmp/e2e-call-video/.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch), locally: calls are incomplete UI, which the test
 *   turns on for both accounts - test agents are admins only on a local server.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/call-video-no-preview.test.ts --config vitest.config.e2e.ts
 */

import * as fs from 'fs';
import * as path from 'path';
import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import type { BrowserContext, Page } from 'playwright';
import {
    BASE_URL, TEST_EMAIL, TEST_EMAIL_2, connectBrowser, ensureSignedIn, setIncompleteUI, skipOnboarding,
    withUILanguage,
    type BrowserConnection,
} from './helpers';
import { SPEECH_WAV } from './video-call';

const SHOTS_DIR = path.join(process.cwd(), 'tmp', 'e2e-call-video');
fs.mkdirSync(SHOTS_DIR, { recursive: true });
const shot = (name: string) => path.join(SHOTS_DIR, `${name}.png`);

const CALL_SCREEN = '.full-screen-call-view.in-call';
const JOIN_PREVIEW = '.modal .camera-preview-video';
const OWN_VIDEO = '.video-panel .video-streaming-preview';
const REMOTE_VIDEO = '.video-panel .remote-video-container';

const NARROW = { width: 390, height: 844 };
const WIDE = { width: 1440, height: 900 };

interface Users {
    conn: BrowserConnection;
    contexts: BrowserContext[];
    caller: Page;
    callee: Page;
}

async function newUserPage(
    conn: BrowserConnection,
    email: string,
    viewport: { width: number; height: number },
): Promise<{ context: BrowserContext; page: Page }> {
    const isNarrow = viewport.width < 768;
    // No touch: the ring buttons attach a swipe-to-answer controller on touch devices.
    const context = await conn.browser.newContext({
        ignoreHTTPSErrors: true,
        locale: 'en-US',
        viewport,
        deviceScaleFactor: isNarrow ? 2 : 1,
        isMobile: isNarrow,
        hasTouch: false,
    });
    // The fake-device flags answer getUserMedia, but the app reads the Permissions API first.
    await context.grantPermissions(['microphone', 'camera'], { origin: BASE_URL });
    const page = await context.newPage();
    page.on('pageerror', e => console.log(`PAGEERROR[${email}]:`, e.message));
    await ensureSignedIn(page, email);
    return { context, page };
}

async function signInBoth(viewport: { width: number; height: number }): Promise<Users> {
    // Real speech, not the fake mic's beep: a silent recording idles out, and the call with it.
    const conn = await connectBrowser({ fakeAudioFile: SPEECH_WAV });
    // Sequential sign-ins: parallel ones race on the shared server flow (see vitest.config.e2e.ts).
    const caller = await newUserPage(conn, TEST_EMAIL, viewport);
    const callee = await newUserPage(conn, TEST_EMAIL_2, viewport);
    for (const page of [caller.page, callee.page]) {
        await skipOnboarding(page);
        await setIncompleteUI(page, true);
    }
    return { conn, contexts: [caller.context, callee.context], caller: caller.page, callee: callee.page };
}

async function signOutBoth(users: Users | undefined) {
    if (!users)
        return;

    // Unload before closing: a context closed outright leaves its server-side circuit alive for
    // about a minute, still ringing and heartbeating as this account into the next run.
    for (const page of [users.caller, users.callee]) {
        await setIncompleteUI(page, false).catch(() => { /* ignore */ });
        await page.goto('about:blank').catch(() => { /* ignore */ });
    }
    for (const context of users.contexts)
        await context.close().catch(() => { /* ignore */ });
    if (users.conn.ownsBrowser) {
        await users.conn.context.close().catch(() => { /* ignore */ });
        await users.conn.browser.close().catch(() => { /* ignore */ });
    }
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

/** Opens the peer chat on both sides, dials from the caller and accepts on the callee. */
async function startCall({ caller, callee }: Users) {
    // A reply from the callee opens the peer-call gate for the caller
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
    // On a wide screen an active call has no screen of its own: it ends with the recording
    const recordOn = page.locator('.chat-audio-panel .recorder-wrapper.record-on').first();
    if (await recordOn.isVisible().catch(() => false))
        await page.locator('.chat-audio-panel .recorder-wrapper button').first().click().catch(() => { /* ignore */ });
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

function isShown(page: Page, selector: string): Promise<boolean> {
    return page.locator(selector).first().isVisible().catch(() => false);
}

describe('camera on during a call, narrow screen', () => {
    let users: Users;

    beforeAll(async () => {
        users = await signInBoth(NARROW);
    }, 180_000);

    afterEach(async () => {
        await hangUpIfAny(users.caller);
        await hangUpIfAny(users.callee);
    }, 30_000);

    afterAll(async () => {
        await signOutBoth(users);
    }, 60_000);

    it('starts video at once, full-screen, without the join preview', async () => {
        // arrange
        const { caller, callee } = users;
        await startCall(users);
        for (const page of [caller, callee])
            await page.locator(`${CALL_SCREEN} .c-toolbar`).first().waitFor({ state: 'visible', timeout: 30_000 });
        await caller.screenshot({ path: shot('narrow-1-caller-call-screen') });

        // act - the caller turns the camera on from the call screen
        let hasSeenJoinPreview = false;
        let hasSeenChat = false;
        await caller.locator(`${CALL_SCREEN} .c-toolbar .btn-video-toggle`).first().click();
        await expect.poll(async () => {
            hasSeenJoinPreview ||= await isShown(caller, JOIN_PREVIEW);
            const isCovered = await isVideoCoveringScreen(caller);
            // Neither the call screen nor the video covering the screen means the chat showed in between
            hasSeenChat ||= !isCovered && !await isShown(caller, CALL_SCREEN);
            return isCovered;
        }, { timeout: 30_000, interval: 50 }).toBe(true);

        // assert - the camera is on, full-screen, with nothing asked or shown in between
        expect(hasSeenJoinPreview, 'mid-call the camera starts without the join preview').toBe(false);
        expect(hasSeenChat, 'the call screen stays up until the video covers the screen').toBe(false);
        await caller.locator(CALL_SCREEN).first().waitFor({ state: 'hidden', timeout: 10_000 });
        await caller.locator('.video-panel.expanded .video-streaming-preview').first()
            .waitFor({ state: 'visible', timeout: 20_000 });
        await caller.waitForTimeout(1_500);
        await caller.screenshot({ path: shot('narrow-2-caller-video-full-screen') });

        // assert - the other side gets the video, in its chat under the call screen
        await callee.screenshot({ path: shot('narrow-3-callee-still-on-call-screen') });
        await callee.locator(`${CALL_SCREEN} .c-call-bar .btn-video-panel`).first().click();
        await callee.locator(REMOTE_VIDEO).first().waitFor({ state: 'visible', timeout: 30_000 });
        await callee.waitForTimeout(1_500);
        await callee.screenshot({ path: shot('narrow-4-callee-video-in-chat') });
    }, 180_000);
});

describe('camera on during a call, wide screen', () => {
    let users: Users;

    beforeAll(async () => {
        users = await signInBoth(WIDE);
    }, 180_000);

    afterEach(async () => {
        await hangUpIfAny(users.caller);
        await hangUpIfAny(users.callee);
    }, 30_000);

    afterAll(async () => {
        await signOutBoth(users);
    }, 60_000);

    it('starts video at once, full-screen, without the join preview', async () => {
        // arrange - on a wide screen an active call has no call screen: it is in the chat
        const { caller, callee } = users;
        await startCall(users);
        const videoToggle = caller.locator('.chat-audio-panel .video-wrapper button').first();
        await caller.locator('.chat-audio-panel .recorder-wrapper.record-on:not(.applying-changes)').first()
            .waitFor({ state: 'attached', timeout: 30_000 });
        await videoToggle.waitFor({ state: 'visible', timeout: 30_000 });
        await caller.screenshot({ path: shot('wide-1-caller-in-call') });

        // act - the caller turns the camera on from the chat's audio panel
        let hasSeenJoinPreview = false;
        await videoToggle.click();
        await expect.poll(async () => {
            hasSeenJoinPreview ||= await isShown(caller, JOIN_PREVIEW);
            return isShown(caller, OWN_VIDEO);
        }, { timeout: 30_000, interval: 50 }).toBe(true);

        // assert - the camera is on with nothing asked, and a call's video opens full-screen
        expect(hasSeenJoinPreview, 'mid-call the camera starts without the join preview').toBe(false);
        await caller.locator('.video-panel.expanded .video-streaming-preview').first()
            .waitFor({ state: 'visible', timeout: 20_000 });
        await caller.waitForTimeout(1_500);
        await caller.screenshot({ path: shot('wide-2-caller-video-full-screen') });

        // assert - the other side gets the video
        await callee.locator(REMOTE_VIDEO).first().waitFor({ state: 'visible', timeout: 30_000 });
        await callee.waitForTimeout(1_500);
        await callee.screenshot({ path: shot('wide-3-callee-video-inline') });
    }, 180_000);
});
