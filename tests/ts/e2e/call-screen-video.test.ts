/**
 * E2E test: a call keeps one screen while its video starts and ends (#5049).
 *
 * The call screen has a header, a control bar and a stage between them; the stage shows the
 * peer's avatar without video and the video tiles with it. Two users are on a call, both on the
 * call screen of a phone-sized viewport:
 *   - the caller turns the camera on: both stages switch to video;
 *   - the caller turns the camera off: both stages switch back to the avatar.
 * Through both switches the screen, its header and its control bar must stay the very same
 * elements on both sides - nothing is swapped for another screen, and the chat never shows.
 *
 * Screenshots go to tmp/e2e-call-screen-video/.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch), locally - see peer-call.ts.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/call-screen-video.test.ts --config vitest.config.e2e.ts
 */

import * as fs from 'fs';
import * as path from 'path';
import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import type { Page } from 'playwright';
import {
    CALL_CONTROLS, CALL_SCREEN, NARROW, hangUpIfAny, isShown, isVideoCoveringScreen, signInBoth, signOutBoth,
    startPeerCall,
    type Users,
} from './peer-call';

const SHOTS_DIR = path.join(process.cwd(), 'tmp', 'e2e-call-screen-video');
fs.mkdirSync(SHOTS_DIR, { recursive: true });
const shot = (name: string) => path.join(SHOTS_DIR, `${name}.png`);

const AVATAR_STAGE = `${CALL_SCREEN} .call-screen-body`;
const SCREEN_PARTS = [CALL_SCREEN, `${CALL_SCREEN} .call-screen-header`, CALL_CONTROLS];

type MarkedElement = Element & { e2eScreenMark?: boolean };

/** Stamps the screen, its header and its control bar, so a later check can tell them from recreated ones. */
async function markScreenParts(page: Page) {
    await page.evaluate(selectors => {
        for (const selector of selectors)
            document.querySelector<MarkedElement>(selector)!.e2eScreenMark = true;
    }, SCREEN_PARTS);
}

/** The parts of the screen that no longer carry the stamp - recreated, or gone. */
function listReplacedScreenParts(page: Page): Promise<string[]> {
    return page.evaluate(
        selectors => selectors.filter(s => document.querySelector<MarkedElement>(s)?.e2eScreenMark !== true),
        SCREEN_PARTS);
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

    it('stays the same screen while video starts and ends', async () => {
        // arrange - both on the call screen, with the avatar on its stage
        const { caller, callee } = users;
        await startPeerCall(caller, callee);
        for (const page of [caller, callee]) {
            await page.locator(CALL_CONTROLS).first().waitFor({ state: 'visible', timeout: 30_000 });
            await page.locator(AVATAR_STAGE).first().waitFor({ state: 'visible', timeout: 10_000 });
            await markScreenParts(page);
        }
        await callee.screenshot({ path: shot('1-callee-call-screen') });

        // act - the caller turns the camera on
        const hasLeftScreen = { caller: false, callee: false };
        await caller.locator(`${CALL_CONTROLS} .btn-video-toggle`).first().click();
        await expect.poll(async () => {
            hasLeftScreen.caller ||= !await isShown(caller, CALL_SCREEN);
            hasLeftScreen.callee ||= !await isShown(callee, CALL_SCREEN);
            return await isVideoCoveringScreen(caller) && await isVideoCoveringScreen(callee);
        }, { timeout: 30_000, interval: 50 }).toBe(true);

        // assert - the video is on the stage of the screen that was already there
        expect(hasLeftScreen, 'the call screen stays up while its video starts')
            .toEqual({ caller: false, callee: false });
        await callee.locator(`${CALL_SCREEN} .remote-video-container`).first()
            .waitFor({ state: 'visible', timeout: 30_000 });
        await caller.locator(`${CALL_SCREEN} .video-streaming-preview`).first()
            .waitFor({ state: 'visible', timeout: 20_000 });
        for (const [who, page] of [['caller', caller], ['callee', callee]] as const) {
            expect(await listReplacedScreenParts(page), `${who}: video comes up inside the same screen`).toEqual([]);
            expect(await isShown(page, AVATAR_STAGE), `${who}: the video takes the stage over`).toBe(false);
        }
        await callee.waitForTimeout(1_500);
        await caller.screenshot({ path: shot('2-caller-video-on-call-screen') });
        await callee.screenshot({ path: shot('2-callee-video-on-call-screen') });

        // act - the caller turns the camera off, the last video of the call
        await caller.locator(`${CALL_CONTROLS} .btn-video-toggle`).first().click();
        await expect.poll(async () => {
            hasLeftScreen.caller ||= !await isShown(caller, CALL_SCREEN);
            hasLeftScreen.callee ||= !await isShown(callee, CALL_SCREEN);
            return await isShown(caller, AVATAR_STAGE) && await isShown(callee, AVATAR_STAGE);
        }, { timeout: 30_000, interval: 50 }).toBe(true);

        // assert - both stages are back to the avatar, on the same screen still
        expect(hasLeftScreen, 'the call screen stays up while its video ends')
            .toEqual({ caller: false, callee: false });
        for (const [who, page] of [['caller', caller], ['callee', callee]] as const) {
            expect(await listReplacedScreenParts(page), `${who}: the screen outlives its video`).toEqual([]);
            expect(await isVideoCoveringScreen(page), `${who}: no video is left on the stage`).toBe(false);
        }
        await caller.screenshot({ path: shot('3-caller-back-to-avatar') });
        await callee.screenshot({ path: shot('3-callee-back-to-avatar') });
    }, 180_000);

    it('has the same control bar with and without video', async () => {
        // arrange
        const { caller, callee } = users;
        await startPeerCall(caller, callee);
        const controls = caller.locator(CALL_CONTROLS).first();
        await controls.waitFor({ state: 'visible', timeout: 30_000 });
        // The controls sit in the bar's two sides, around the recorder
        const listControls = () => controls.evaluate(bar =>
            [...bar.querySelectorAll(':scope > :not(.c-side), :scope > .c-side > *')].map(e => ({
                name: [...e.classList].find(c => c.startsWith('btn-') && c !== 'btn-glass' && c !== 'btn-h')
                    ?? e.classList[0],
                left: Math.round(e.getBoundingClientRect().left),
            })));
        const audioControls = await listControls();

        // act - the camera adds its switch button; turned off again, the bar is as it was
        await caller.locator(`${CALL_CONTROLS} .btn-video-toggle`).first().click();
        await expect.poll(() => isVideoCoveringScreen(caller), { timeout: 30_000 }).toBe(true);
        const videoControls = await listControls();

        // assert - only the camera switch is new, after the recorder, and nothing moved
        expect(videoControls.filter(c => c.name !== 'btn-camera-switch')).toEqual(audioControls);
        expect(videoControls.map(c => c.name).slice(0, 3))
            .toEqual(['btn-video-toggle', 'recorder-wrapper', 'btn-camera-switch']);
    }, 180_000);

    it('collapses on Back, with the call going on in the chat it was started from', async () => {
        // arrange
        const { caller, callee } = users;
        await startPeerCall(caller, callee);
        await caller.locator(CALL_CONTROLS).first().waitFor({ state: 'visible', timeout: 30_000 });
        const chatPath = new URL(caller.url()).pathname;
        await caller.waitForTimeout(1_000);

        // act
        await caller.goBack();

        // assert - Back takes the screen away, not the chat under it, and never hangs up
        await caller.locator(CALL_SCREEN).first().waitFor({ state: 'hidden', timeout: 10_000 });
        await caller.locator('.collapsed-call-view.in-call').first().waitFor({ state: 'visible', timeout: 10_000 });
        expect(new URL(caller.url()).pathname).toBe(chatPath);
        expect(await isShown(callee, CALL_SCREEN), 'the other side is still on the call').toBe(true);

        // act - and the island brings the same screen back
        await caller.locator('.collapsed-call-view.in-call .c-in-call').first().click();

        // assert
        await caller.locator(CALL_CONTROLS).first().waitFor({ state: 'visible', timeout: 10_000 });
    }, 180_000);

    it('closes an open menu on Escape, collapses on the next one, and comes back from the island', async () => {
        // arrange - the call screen with its menu open
        const { caller, callee } = users;
        await startPeerCall(caller, callee);
        await caller.locator(`${CALL_SCREEN} .call-screen-header .btn-video-menu`).first().click();
        const menu = caller.locator('.video-panel-menu').first();
        await menu.waitFor({ state: 'visible', timeout: 10_000 });

        // act
        await caller.keyboard.press('Escape');
        await menu.waitFor({ state: 'hidden', timeout: 10_000 });
        // Longer than the screen takes to animate away, during which it still counts as visible
        await caller.waitForTimeout(1_000);

        // assert
        expect(await isShown(caller, CALL_SCREEN), 'Escape with a menu open closes the menu only').toBe(true);

        // act
        await caller.keyboard.press('Escape');

        // assert - the call goes on in the island
        await caller.locator(CALL_SCREEN).first().waitFor({ state: 'hidden', timeout: 10_000 });
        await caller.locator('.collapsed-call-view.in-call').first().waitFor({ state: 'visible', timeout: 10_000 });

        // act - and the island brings the same screen back
        await caller.locator('.collapsed-call-view.in-call .c-in-call').first().click();

        // assert
        await caller.locator(CALL_CONTROLS).first().waitFor({ state: 'visible', timeout: 10_000 });
    }, 180_000);
});
