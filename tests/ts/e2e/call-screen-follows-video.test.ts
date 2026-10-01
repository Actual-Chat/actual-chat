/**
 * E2E test: on a phone the call screen follows video starting and ending (#5017).
 *
 * A call has two full-screen screens on the narrow layout: the call screen and the expanded video
 * panel, which lives in the chat under it. Two users are on a call, both on the call screen:
 *   - the caller turns the camera on: the callee's call screen must give way to the video,
 *     full-screen - it used to stay up, with the video playing in the chat underneath;
 *   - the caller turns the camera off: both must be back on the call screen - they used to land
 *     in the chat, under the in-call island.
 * Neither switch may show the chat in between.
 *
 * Screenshots go to tmp/e2e-call-screen-video/.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch), locally - see peer-call.ts.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/call-screen-follows-video.test.ts --config vitest.config.e2e.ts
 */

import * as fs from 'fs';
import * as path from 'path';
import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import type { Page } from 'playwright';
import {
    CALL_SCREEN, NARROW, hangUpIfAny, isShown, isVideoCoveringScreen, signInBoth, signOutBoth, startPeerCall,
    type Users,
} from './peer-call';

const SHOTS_DIR = path.join(process.cwd(), 'tmp', 'e2e-call-screen-video');
fs.mkdirSync(SHOTS_DIR, { recursive: true });
const shot = (name: string) => path.join(SHOTS_DIR, `${name}.png`);

/** Neither full-screen screen is up, so what shows is the chat under them. */
async function isChatShowing(page: Page): Promise<boolean> {
    return !await isVideoCoveringScreen(page) && !await isShown(page, CALL_SCREEN);
}

describe('call screen and video, narrow screen', () => {
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

    it('gives way to incoming video and comes back when the video ends', async () => {
        // arrange - both on the call screen
        const { caller, callee } = users;
        await startPeerCall(caller, callee);
        for (const page of [caller, callee])
            await page.locator(`${CALL_SCREEN} .c-toolbar`).first().waitFor({ state: 'visible', timeout: 30_000 });
        await callee.screenshot({ path: shot('1-callee-call-screen') });

        // act - the caller turns the camera on
        let hasCalleeSeenChat = false;
        await caller.locator(`${CALL_SCREEN} .c-toolbar .btn-video-toggle`).first().click();
        await expect.poll(async () => {
            hasCalleeSeenChat ||= await isChatShowing(callee);
            return isVideoCoveringScreen(callee);
        }, { timeout: 30_000, interval: 50 }).toBe(true);

        // assert - the callee's call screen gave way to the video, full-screen
        expect(hasCalleeSeenChat, 'the call screen stays up until the video covers the screen').toBe(false);
        await callee.locator(CALL_SCREEN).first().waitFor({ state: 'hidden', timeout: 10_000 });
        await callee.locator('.video-panel.expanded .remote-video-container').first()
            .waitFor({ state: 'visible', timeout: 30_000 });
        await caller.locator('.video-panel.expanded .video-streaming-preview').first()
            .waitFor({ state: 'visible', timeout: 20_000 });
        await callee.waitForTimeout(1_500);
        await callee.screenshot({ path: shot('2-callee-video-full-screen') });

        // act - the caller turns the camera off, the last video of the call
        const hasSeenChat = { caller: false, callee: false };
        await caller.locator('.video-panel.expanded .video-panel-footer .btn-video-toggle').first().click();
        await expect.poll(async () => {
            hasSeenChat.caller ||= await isChatShowing(caller);
            hasSeenChat.callee ||= await isChatShowing(callee);
            return await isShown(caller, CALL_SCREEN) && await isShown(callee, CALL_SCREEN);
        }, { timeout: 30_000, interval: 50 }).toBe(true);

        // assert - both are back on the call screen, with the video panel gone under it
        expect(hasSeenChat, 'the call screen comes back before the video panel goes')
            .toEqual({ caller: false, callee: false });
        for (const page of [caller, callee]) {
            await page.locator('.video-panel').first().waitFor({ state: 'hidden', timeout: 15_000 });
            expect(await isShown(page, CALL_SCREEN), 'the call screen stays once the panel is gone').toBe(true);
        }
        await caller.screenshot({ path: shot('3-caller-back-on-call-screen') });
        await callee.screenshot({ path: shot('3-callee-back-on-call-screen') });
    }, 180_000);
});
