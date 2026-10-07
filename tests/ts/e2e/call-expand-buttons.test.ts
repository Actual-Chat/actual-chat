/**
 * E2E test: during a call, every expand button leads back to the one call screen (#5015, #5049).
 *
 * Two users are on a call, and the caller's camera is on:
 *   - narrow (phone-sized viewport): the inline video stands in for the in-call island, with the
 *     call's timer and hang-up, and its expand button opens the call screen with the video on its
 *     stage, on the side that sends it and on the side that only receives it. In another chat the
 *     video floats; expanded from there, Back stays in that chat, and the call's chat takes the
 *     video back inline. With no video the island opens the same screen, with the avatar on its
 *     stage.
 *   - wide: an active call has neither an island nor a screen of its own, so the inline video's
 *     button is the only one, and it opens the call screen with the video. In another chat the
 *     video floats as well.
 *
 * Screenshots go to tmp/e2e-call-expand/.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch), locally: calls are incomplete UI, which the test
 *   turns on for both accounts - test agents are admins only on a local server.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/call-expand-buttons.test.ts --config vitest.config.e2e.ts
 */

import * as fs from 'fs';
import * as path from 'path';
import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import type { Page } from 'playwright';
import { DEFAULT_CHAT_URL } from './helpers';
import {
    CALL_COLLAPSE, CALL_CONTROLS, CALL_SCREEN, NARROW, WIDE, hangUpIfAny, isShown, isVideoCoveringScreen, signInBoth,
    signOutBoth, startPeerCall,
    type Users,
} from './peer-call';
import { collapseVideoPanel, expandVideoPanel } from './video-call';

const SHOTS_DIR = path.join(process.cwd(), 'tmp', 'e2e-call-expand');
fs.mkdirSync(SHOTS_DIR, { recursive: true });
const shot = (name: string) => path.join(SHOTS_DIR, `${name}.png`);

const ISLAND = '.collapsed-call-view.in-call .c-in-call';
const INLINE_VIDEO = '.call-screen:not(.expanded):not(.collapsed)';
const FLOATING_VIDEO = '.call-screen.collapsed';
const OWN_TILE = '.video-streaming-preview';
const REMOTE_TILE = '.remote-video-container';

/** Waits for the call's video in the chat, standing in for the island: with the call's timer and hang-up. */
async function waitForInlineVideo(page: Page, tile: string) {
    await page.locator(`${INLINE_VIDEO} ${tile}`).first().waitFor({ state: 'visible', timeout: 30_000 });
    for (const part of ['.c-call-timer', '.btn-hang-up'])
        await page.locator(`${INLINE_VIDEO} .call-screen-header ${part}`).first()
            .waitFor({ state: 'visible', timeout: 10_000 });
    expect(await isShown(page, ISLAND), 'the video stands in for the island').toBe(false);
}

type WatchedVideo = HTMLVideoElement & { e2eWatched?: boolean };
type PauseLog = Window & { e2ePauses?: number };

/** Stamps the call screen's playing videos and counts their pauses from here on; returns how many it watches. */
function watchPlayingVideos(page: Page): Promise<number> {
    return page.evaluate(() => {
        const videos = [...document.querySelectorAll<HTMLVideoElement>('.call-screen video')].filter(v => !v.paused);
        (window as PauseLog).e2ePauses = 0;
        for (const video of videos as WatchedVideo[]) {
            video.e2eWatched = true;
            video.addEventListener('pause', () => { (window as PauseLog).e2ePauses! += 1; });
        }
        return videos.length;
    });
}

/** The pauses counted since watchPlayingVideos, and how many of its videos still play in the call screen. */
function readWatchedVideos(page: Page): Promise<{ pauses: number; playing: number }> {
    return page.evaluate(() => {
        const videos = [...document.querySelectorAll<WatchedVideo>('.call-screen video')].filter(v => v.e2eWatched);
        return {
            pauses: (window as PauseLog).e2ePauses ?? -1,
            playing: videos.filter(v => !v.paused).length,
        };
    });
}

async function navigateTo(page: Page, path: string) {
    await page.evaluate(p => (window as unknown as { debugUI: { navigateTo(url: string): void } })
        .debugUI.navigateTo(p), path);
}

describe('expand buttons during a call with video, narrow screen', () => {
    let users: Users;

    beforeAll(async () => {
        users = await signInBoth(NARROW);
    }, 180_000);

    afterEach(async () => {
        await hangUpIfAny(users.caller);
        await hangUpIfAny(users.callee);
    }, 60_000);

    afterAll(async () => {
        await signOutBoth(users);
    }, 60_000);

    it('opens the call screen with its video from the inline video, which stands in for the island', async () => {
        // arrange - the caller's camera is on, and both sides are back in the chat
        const { caller, callee } = users;
        await startPeerCall(caller, callee);
        for (const page of [caller, callee])
            await page.locator(CALL_CONTROLS).first().waitFor({ state: 'visible', timeout: 30_000 });
        await caller.locator(`${CALL_CONTROLS} .btn-video-toggle`).first().click();
        await expect.poll(() => isVideoCoveringScreen(caller), { timeout: 30_000 }).toBe(true);
        await collapseVideoPanel(caller);
        await waitForInlineVideo(caller, OWN_TILE);
        await caller.waitForTimeout(1_000);
        await caller.screenshot({ path: shot('narrow-1-caller-chat-with-inline-video') });
        // Incoming video came up on the callee's call screen as well
        await expect.poll(() => isVideoCoveringScreen(callee), { timeout: 30_000 }).toBe(true);
        await collapseVideoPanel(callee);
        await waitForInlineVideo(callee, REMOTE_TILE);
        await callee.waitForTimeout(1_000);
        await callee.screenshot({ path: shot('narrow-2-callee-chat-with-inline-video') });

        // act
        await expandVideoPanel(caller);

        // assert - the inline video's button leads to the call screen, with the video on its stage
        await expect.poll(() => isVideoCoveringScreen(caller), { timeout: 20_000 }).toBe(true);
        expect(await isShown(caller, CALL_CONTROLS), 'the video opens on the call screen, with its controls')
            .toBe(true);
        await caller.waitForTimeout(1_000);
        await caller.screenshot({ path: shot('narrow-3-caller-video-from-inline-video') });

        // act - the callee, who only receives the video, expands it too
        await expandVideoPanel(callee);

        // assert - incoming video opens on the call screen the same way
        await callee.locator(`${CALL_SCREEN} ${REMOTE_TILE}`).first().waitFor({ state: 'visible', timeout: 20_000 });
        await callee.waitForTimeout(1_000);
        await callee.screenshot({ path: shot('narrow-4-callee-video-from-inline-video') });
    }, 240_000);

    it('floats the video in another chat, and Back from its screen stays there', async () => {
        // arrange - the caller's camera is on, shown inline in the call's chat
        const { caller, callee } = users;
        await startPeerCall(caller, callee);
        await caller.locator(CALL_CONTROLS).first().waitFor({ state: 'visible', timeout: 30_000 });
        await caller.locator(`${CALL_CONTROLS} .btn-video-toggle`).first().click();
        await expect.poll(() => isVideoCoveringScreen(caller), { timeout: 30_000 }).toBe(true);
        await collapseVideoPanel(caller);
        await waitForInlineVideo(caller, OWN_TILE);
        const callChatPath = new URL(caller.url()).pathname;
        const otherChatPath = new URL(DEFAULT_CHAT_URL).pathname;

        // act - the caller leaves for another chat
        await navigateTo(caller, otherChatPath);

        // assert - the video floats along, with the call's controls, and the island stays away
        await caller.locator(`${FLOATING_VIDEO} ${OWN_TILE}`).first().waitFor({ state: 'visible', timeout: 20_000 });
        await caller.locator(`${FLOATING_VIDEO} .call-screen-header .btn-hang-up`).first()
            .waitFor({ state: 'visible', timeout: 10_000 });
        expect(await isShown(caller, ISLAND), 'the floating video stands in for the island').toBe(false);
        await caller.waitForTimeout(1_000);
        await caller.screenshot({ path: shot('narrow-5-caller-video-floating-in-another-chat') });

        // act - expanded from the floating video, then Back
        await expandVideoPanel(caller);
        await expect.poll(() => isVideoCoveringScreen(caller), { timeout: 20_000 }).toBe(true);
        expect(new URL(caller.url()).pathname, 'the call screen is not a part of any chat').toBe(otherChatPath);
        await caller.waitForTimeout(1_000);
        await caller.goBack();

        // assert - back where the screen was opened from, with the video floating again
        await caller.locator(FLOATING_VIDEO).first().waitFor({ state: 'visible', timeout: 20_000 });
        expect(new URL(caller.url()).pathname).toBe(otherChatPath);
        expect(await isShown(caller, CALL_SCREEN)).toBe(false);
        await caller.waitForTimeout(1_000);
        await caller.screenshot({ path: shot('narrow-6-caller-back-in-another-chat') });

        // act - the caller returns to the call's chat
        await navigateTo(caller, callChatPath);

        // assert - the video is back inline
        await waitForInlineVideo(caller, OWN_TILE);
        expect(await isShown(caller, FLOATING_VIDEO)).toBe(false);
        await caller.waitForTimeout(1_000);
        await caller.screenshot({ path: shot('narrow-7-caller-video-inline-again') });
    }, 240_000);

    it('keeps the video playing while it moves from the chat header to the floating island', async () => {
        // arrange - the callee watches the caller's video inline, in the call's chat
        const { caller, callee } = users;
        await startPeerCall(caller, callee);
        await caller.locator(CALL_CONTROLS).first().waitFor({ state: 'visible', timeout: 30_000 });
        await caller.locator(`${CALL_CONTROLS} .btn-video-toggle`).first().click();
        await expect.poll(() => isVideoCoveringScreen(callee), { timeout: 30_000 }).toBe(true);
        await collapseVideoPanel(callee);
        await waitForInlineVideo(callee, REMOTE_TILE);
        await expect.poll(() => callee.evaluate(() =>
            [...document.querySelectorAll<HTMLVideoElement>('.call-screen video')].some(v => !v.paused)),
        { timeout: 20_000 }).toBe(true);
        const watchedCount = await watchPlayingVideos(callee);

        // act - the chat page goes, and the header that held the video with it
        await navigateTo(callee, new URL(DEFAULT_CHAT_URL).pathname);
        await callee.locator(`${FLOATING_VIDEO} ${REMOTE_TILE}`).first().waitFor({ state: 'visible', timeout: 20_000 });
        await callee.waitForTimeout(1_000);

        // assert - the same video elements moved along, and never stopped
        expect(await readWatchedVideos(callee)).toEqual({ pauses: 0, playing: watchedCount });
    }, 240_000);

    it('opens the call screen from the island when the call has no video', async () => {
        // arrange - a call without video, collapsed into the island
        const { caller, callee } = users;
        await startPeerCall(caller, callee);
        await caller.locator(CALL_CONTROLS).first().waitFor({ state: 'visible', timeout: 30_000 });
        await caller.locator(CALL_COLLAPSE).first().click();
        await caller.locator(ISLAND).first().waitFor({ state: 'visible', timeout: 10_000 });
        await caller.waitForTimeout(1_000);
        await caller.screenshot({ path: shot('narrow-8-caller-chat-without-video') });

        // act
        await caller.locator(ISLAND).first().click();

        // assert - the island brings the call screen back, with the avatar on its stage
        await caller.locator(`${CALL_SCREEN} .call-screen-body`).first().waitFor({ state: 'visible', timeout: 10_000 });
        expect(await isVideoCoveringScreen(caller), 'a call without video has none on its stage').toBe(false);
        await caller.waitForTimeout(1_000);
        await caller.screenshot({ path: shot('narrow-9-caller-call-screen-from-island') });
    }, 180_000);
});

describe('expand button during a call with video, wide screen', () => {
    let users: Users;

    beforeAll(async () => {
        users = await signInBoth(WIDE);
    }, 180_000);

    afterEach(async () => {
        await hangUpIfAny(users.caller);
        await hangUpIfAny(users.callee);
    }, 60_000);

    afterAll(async () => {
        await signOutBoth(users);
    }, 60_000);

    it('opens the call screen from the inline video, the only expand button there', async () => {
        // arrange - on a wide screen the call is in the chat; the caller's camera is on, shown inline
        const { caller, callee } = users;
        await startPeerCall(caller, callee);
        const videoToggle = caller.locator('.chat-audio-panel .video-wrapper button').first();
        await caller.locator('.chat-audio-panel .recorder-wrapper.record-on:not(.applying-changes)').first()
            .waitFor({ state: 'attached', timeout: 30_000 });
        await videoToggle.waitFor({ state: 'visible', timeout: 30_000 });
        await videoToggle.click();
        await caller.locator(`.call-screen.expanded ${OWN_TILE}`).first()
            .waitFor({ state: 'visible', timeout: 30_000 });
        await collapseVideoPanel(caller);
        await caller.locator(`${INLINE_VIDEO} ${OWN_TILE}`).first()
            .waitFor({ state: 'visible', timeout: 20_000 });
        await caller.waitForTimeout(1_000);
        await caller.screenshot({ path: shot('wide-1-caller-video-inline') });

        // assert - with its video inline, a wide call has no island and no screen of its own
        expect(await isShown(caller, '.collapsed-call-view'), 'no in-call island on a wide screen').toBe(false);
        expect(await isShown(caller, CALL_SCREEN), 'the call is in the chat on a wide screen').toBe(false);

        // act
        await expandVideoPanel(caller);

        // assert - the inline video's button opens the call screen, with the call's controls
        await expect.poll(() => isVideoCoveringScreen(caller), { timeout: 20_000 }).toBe(true);
        expect(await isShown(caller, CALL_CONTROLS), 'the full-screen video is the call screen').toBe(true);
        await caller.waitForTimeout(1_000);
        await caller.screenshot({ path: shot('wide-2-caller-video-full-screen') });

        // act - the callee, who only receives the video, expands it too
        await callee.locator(`${INLINE_VIDEO} ${REMOTE_TILE}`).first()
            .waitFor({ state: 'visible', timeout: 30_000 });
        await callee.waitForTimeout(1_000);
        await callee.screenshot({ path: shot('wide-3-callee-video-inline') });
        await expandVideoPanel(callee);

        // assert
        await expect.poll(() => isVideoCoveringScreen(callee), { timeout: 20_000 }).toBe(true);
        expect(await isShown(callee, '.collapsed-call-view'), 'no in-call island on a wide screen').toBe(false);
        await callee.waitForTimeout(1_000);
        await callee.screenshot({ path: shot('wide-4-callee-video-full-screen') });
    }, 240_000);

    it('floats the video in another chat and puts it back inline on the return', async () => {
        // arrange - the caller's camera is on, shown inline
        const { caller, callee } = users;
        await startPeerCall(caller, callee);
        const videoToggle = caller.locator('.chat-audio-panel .video-wrapper button').first();
        await caller.locator('.chat-audio-panel .recorder-wrapper.record-on:not(.applying-changes)').first()
            .waitFor({ state: 'attached', timeout: 30_000 });
        await videoToggle.click();
        await caller.locator(`.call-screen.expanded ${OWN_TILE}`).first()
            .waitFor({ state: 'visible', timeout: 30_000 });
        await collapseVideoPanel(caller);
        await waitForInlineVideo(caller, OWN_TILE);
        const callChatPath = new URL(caller.url()).pathname;

        // act
        await navigateTo(caller, new URL(DEFAULT_CHAT_URL).pathname);

        // assert
        await caller.locator(`${FLOATING_VIDEO} ${OWN_TILE}`).first().waitFor({ state: 'visible', timeout: 20_000 });
        await caller.waitForTimeout(1_000);
        await caller.screenshot({ path: shot('wide-5-caller-video-floating-in-another-chat') });

        // act
        await navigateTo(caller, callChatPath);

        // assert
        await waitForInlineVideo(caller, OWN_TILE);
        expect(await isShown(caller, FLOATING_VIDEO)).toBe(false);
    }, 240_000);
});
