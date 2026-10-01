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
 * Outside a call the preview stays (#5016): with only the mic open in a chat, the camera button
 * must open the join preview on both layouts, and the camera goes live only once it is confirmed.
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
import { TEST_EMAIL, connectBrowser, type BrowserConnection } from './helpers';
import {
    CALL_SCREEN, JOIN_PREVIEW, NARROW, OWN_VIDEO, REMOTE_VIDEO, WIDE, hangUpIfAny, isShown, isVideoCoveringScreen,
    newUserPage, signInBoth, signOutBoth, startPeerCall,
    type Users,
} from './peer-call';
import { SPEECH_WAV, hangUpIfAny as leaveVideoSession, openCallChat, startRecording } from './video-call';

const SHOTS_DIR = path.join(process.cwd(), 'tmp', 'e2e-call-video');
fs.mkdirSync(SHOTS_DIR, { recursive: true });
const shot = (name: string) => path.join(SHOTS_DIR, `${name}.png`);

// Its own contexts, apart from the call tests: confirming the preview saves the camera, and headless
// Chromium renames its fake camera on every page load - a later start without the preview would fail.
describe('camera on with only the mic open', () => {
    let conn: BrowserConnection;

    beforeAll(async () => {
        conn = await connectBrowser({ fakeAudioFile: SPEECH_WAV });
    }, 60_000);

    afterAll(async () => {
        if (conn.ownsBrowser) {
            await conn.context.close().catch(() => { /* ignore */ });
            await conn.browser.close().catch(() => { /* ignore */ });
        }
    }, 60_000);

    it.each([
        ['narrow', NARROW],
        ['wide', WIDE],
    ] as const)('asks through the join preview first, %s screen', async (layout, viewport) => {
        // arrange - no call and no video session in the chat, just this user recording
        const { context, page } = await newUserPage(conn, TEST_EMAIL, viewport);
        try {
            await openCallChat(page);
            await startRecording(page);
            const videoToggle = page.locator('.chat-audio-panel .video-wrapper button').first();
            await videoToggle.waitFor({ state: 'visible', timeout: 30_000 });
            await page.screenshot({ path: shot(`${layout}-mic-1-mic-open`) });

            // act
            await videoToggle.click();
            await expect.poll(async () => await isShown(page, JOIN_PREVIEW) || await isShown(page, OWN_VIDEO), {
                timeout: 30_000,
                interval: 50,
            }).toBe(true);

            // assert - the preview asks first, and nothing is live behind it
            expect(await isShown(page, OWN_VIDEO), 'outside a call the camera does not go live unasked').toBe(false);
            expect(await isShown(page, JOIN_PREVIEW), 'outside a call the camera button opens the preview').toBe(true);
            const modal = page.locator('.modal').filter({ has: page.locator('.camera-preview-video') }).first();
            const submit = modal.locator('.btn-modal.btn-primary').first();
            await expect.poll(async () => submit.isEnabled(), { timeout: 15_000 }).toBe(true);
            await page.waitForTimeout(1_500);
            await page.screenshot({ path: shot(`${layout}-mic-2-join-preview`) });

            // act - the preview is confirmed
            await submit.click();

            // assert - the camera goes live, in the chat rather than full-screen
            await page.locator(OWN_VIDEO).first().waitFor({ state: 'visible', timeout: 20_000 });
            await page.waitForTimeout(1_500);
            expect(await isShown(page, '.video-panel.expanded'), 'video outside a call opens inline').toBe(false);
            await page.screenshot({ path: shot(`${layout}-mic-3-video-inline`) });
        }
        finally {
            await leaveVideoSession(page);
            // Unload before closing, so the circuit doesn't keep recording as this account into the call tests
            await page.goto('about:blank').catch(() => { /* ignore */ });
            await context.close().catch(() => { /* ignore */ });
        }
    }, 180_000);
});

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
        await startPeerCall(caller, callee);
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

        // assert - the other side gets the video, full-screen as well (see call-screen-follows-video.test.ts)
        await callee.locator('.video-panel.expanded .remote-video-container').first()
            .waitFor({ state: 'visible', timeout: 30_000 });
        await callee.waitForTimeout(1_500);
        await callee.screenshot({ path: shot('narrow-3-callee-video-full-screen') });
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
        await startPeerCall(caller, callee);
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
