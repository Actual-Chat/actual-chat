/**
 * E2E test: during a call, every expand button leads back to the one call screen (#5015, #5049).
 *
 * Two users are on a call, and the caller's camera is on:
 *   - narrow (phone-sized viewport): the chat shows two expand buttons - the in-call island's and
 *     the inline video's. Both must open the call screen with the video on its stage, on the side
 *     that sends it and on the side that only receives it. From another chat the island gets there
 *     too, without leaving that chat. With no video the island opens the same screen, with the
 *     avatar on its stage.
 *   - wide: an active call has neither an island nor a screen of its own, so the inline video's
 *     button is the only one, and it opens the call screen with the video.
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
const INLINE_VIDEO = '.call-screen:not(.expanded)';
const OWN_TILE = '.video-streaming-preview';
const REMOTE_TILE = '.remote-video-container';

/** Taps the island and waits for the call screen to come up with the video on its stage. */
async function expandFromIsland(page: Page) {
    await page.locator(ISLAND).first().click();
    await expect.poll(() => isVideoCoveringScreen(page), { timeout: 20_000, interval: 50 }).toBe(true);
}

async function waitForInlineVideo(page: Page, tile: string) {
    await page.locator(`${INLINE_VIDEO} ${tile}`).first().waitFor({ state: 'visible', timeout: 30_000 });
    await page.locator(ISLAND).first().waitFor({ state: 'visible', timeout: 10_000 });
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

    it('opens the call screen with its video from the island and from the inline video alike', async () => {
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
        await caller.screenshot({ path: shot('narrow-1-caller-chat-with-two-expand-buttons') });
        // Incoming video came up on the callee's call screen as well
        await expect.poll(() => isVideoCoveringScreen(callee), { timeout: 30_000 }).toBe(true);
        await collapseVideoPanel(callee);
        await waitForInlineVideo(callee, REMOTE_TILE);
        await callee.waitForTimeout(1_000);
        await callee.screenshot({ path: shot('narrow-2-callee-chat-with-two-expand-buttons') });

        // act - the caller expands from the island
        await expandFromIsland(caller);

        // assert - the island leads to the call screen, with the video on its stage
        await caller.locator(`${CALL_SCREEN} ${OWN_TILE}`).first().waitFor({ state: 'visible', timeout: 20_000 });
        expect(await isShown(caller, ISLAND), 'the island gives way to the screen it stands for').toBe(false);
        await caller.waitForTimeout(1_000);
        await caller.screenshot({ path: shot('narrow-3-caller-video-from-island') });

        // act - the caller goes back to the chat and expands from the inline video
        await collapseVideoPanel(caller);
        await waitForInlineVideo(caller, OWN_TILE);
        await expandVideoPanel(caller);

        // assert - the inline video's button leads to the same screen
        await expect.poll(() => isVideoCoveringScreen(caller), { timeout: 20_000 }).toBe(true);
        expect(await isShown(caller, CALL_CONTROLS), 'the video opens on the call screen, with its controls')
            .toBe(true);
        expect(await isShown(caller, ISLAND), 'the island gives way to the screen it stands for').toBe(false);
        await caller.waitForTimeout(1_000);
        await caller.screenshot({ path: shot('narrow-4-caller-video-from-inline-video') });

        // act - the callee, who only receives the video, expands from the island
        await expandFromIsland(callee);

        // assert - incoming video opens on the call screen the same way
        await callee.locator(`${CALL_SCREEN} ${REMOTE_TILE}`).first().waitFor({ state: 'visible', timeout: 20_000 });
        await callee.waitForTimeout(1_000);
        await callee.screenshot({ path: shot('narrow-5-callee-video-from-island') });
    }, 240_000);

    it('opens the call screen with its video from the island in another chat', async () => {
        // arrange - the caller's camera is on, and the caller has left for another chat
        const { caller, callee } = users;
        await startPeerCall(caller, callee);
        await caller.locator(CALL_CONTROLS).first().waitFor({ state: 'visible', timeout: 30_000 });
        await caller.locator(`${CALL_CONTROLS} .btn-video-toggle`).first().click();
        await expect.poll(() => isVideoCoveringScreen(caller), { timeout: 30_000 }).toBe(true);
        await collapseVideoPanel(caller);
        await waitForInlineVideo(caller, OWN_TILE);
        const otherChatPath = new URL(DEFAULT_CHAT_URL).pathname;
        await caller.evaluate(path => (window as unknown as { debugUI: { navigateTo(url: string): void } })
            .debugUI.navigateTo(path), otherChatPath);
        await caller.locator('.call-screen').first().waitFor({ state: 'hidden', timeout: 20_000 });
        await caller.locator(ISLAND).first().waitFor({ state: 'visible', timeout: 10_000 });
        await caller.waitForTimeout(1_000);
        await caller.screenshot({ path: shot('narrow-6-caller-in-another-chat') });

        // act
        await expandFromIsland(caller);

        // assert - the call screen is not a part of any chat, so the caller stays where they were
        expect(new URL(caller.url()).pathname).toBe(otherChatPath);
        await caller.locator(`${CALL_SCREEN} ${OWN_TILE}`).first().waitFor({ state: 'visible', timeout: 20_000 });
        await caller.waitForTimeout(1_000);
        await caller.screenshot({ path: shot('narrow-7-caller-video-from-island-in-another-chat') });
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
});
