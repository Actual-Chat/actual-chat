/**
 * E2E test: on a phone, a call's participant labels stay inside the safe area (#5200).
 *
 * The phone layout of the call screen lets the video run edge to edge, so a label pinned to its tile's
 * corner ended up under the home indicator and cut by the display's rounded corner. The caller is an
 * iPhone 15 - its viewport, real env(safe-area-inset-*) values and corner radius - on a call with a peer:
 * every visible label must be inside the safe area and clear of the corners with one video and with two,
 * in speaker and grid view, in portrait and after a rotation to landscape.
 *
 * Screenshots go to tmp/e2e-call-label-safe-area/.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch), locally - see peer-call.ts.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/call-label-safe-area.test.ts --config vitest.config.e2e.ts
 */

import * as fs from 'fs';
import * as path from 'path';
import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import type { Page } from 'playwright';
import {
    CALL_CONTROLS, CALL_SCREEN, OWN_VIDEO, REMOTE_VIDEO, hangUpIfAny, signInBoth, signOutBoth, startPeerCall,
    type Users,
} from './peer-call';
import { emulateSafeAreas, SafeAreas, type SafeAreaPreset } from './safe-areas';
import { setGallery } from './video-call';

const SHOTS_DIR = path.join(process.cwd(), 'tmp', 'e2e-call-label-safe-area');
fs.mkdirSync(SHOTS_DIR, { recursive: true });
const shot = (name: string) => path.join(SHOTS_DIR, `${name}.png`);

const Portrait = SafeAreas.Presets.iphone15;
const Landscape = SafeAreas.Presets.iphone15Landscape;

async function expectLabelsInsideSafeArea(page: Page, preset: SafeAreaPreset, isGrid: boolean, labelCount: number) {
    await setGallery(page, isGrid);
    const labels = page.locator(`${CALL_SCREEN} .video-participant-label:visible`);
    await expect.poll(() => labels.count(), { timeout: 15_000 }).toBe(labelCount);
    const what = `${labelCount} label(s), ${isGrid ? 'grid' : 'speaker'} view, ${preset.name}`;
    // Polled: the call screen settles its labels around the footer buttons after a layout change
    await expect.poll(impl, { timeout: 10_000, message: what }).toEqual([]);

    // What the phone would hide of each visible label, e.g. `You: clipped by the bottom-left corner`
    async function impl(): Promise<string[]> {
        const boxes = await page.evaluate(selector =>
            [...document.querySelectorAll(selector)]
                .map(label => ({ text: label.textContent.trim(), box: label.getBoundingClientRect() }))
                .filter(label => label.box.width > 0)
                .map(({ text, box }) => ({ text, x: box.x, y: box.y, width: box.width, height: box.height })),
        `${CALL_SCREEN} .video-participant-label`);
        return boxes.flatMap(box => SafeAreas.getProblems(box, preset).map(problem => `${box.text}: ${problem}`));
    }
}

describe('call participant labels on a phone', () => {
    let users: Users;

    beforeAll(async () => {
        users = await signInBoth(Portrait.viewport, async page => {
            await emulateSafeAreas(page, Portrait);
            // Headless Chromium reports a desktop device; the call screen's phone layout needs body.device-mobile
            await page.addInitScript(() => {
                Object.defineProperty(navigator, 'userAgentData', { get: () => ({ mobile: true }) });
            });
        });
    }, 180_000);

    afterEach(async () => {
        await hangUpIfAny(users.caller);
        await hangUpIfAny(users.callee);
    }, 30_000);

    afterAll(async () => {
        await signOutBoth(users);
    }, 60_000);

    it('keeps every label inside the safe area', async () => {
        // arrange - the caller's own camera fills the phone's screen
        const { caller, callee } = users;
        await startPeerCall(caller, callee);
        await caller.locator(CALL_CONTROLS).first().waitFor({ state: 'visible', timeout: 30_000 });
        await caller.locator(`${CALL_CONTROLS} .btn-video-toggle`).first().click();
        await caller.locator(OWN_VIDEO).first().waitFor({ state: 'visible', timeout: 30_000 });

        // assert - one video
        await expectLabelsInsideSafeArea(caller, Portrait, false, 1);
        await caller.screenshot({ path: shot('1-own-speaker') });
        await expectLabelsInsideSafeArea(caller, Portrait, true, 1);
        await caller.screenshot({ path: shot('2-own-grid') });

        // act - the peer turns the camera on: the big tile is theirs now, and so is the label
        await callee.locator(`${CALL_CONTROLS} .btn-video-toggle`).first().click();
        await caller.locator(REMOTE_VIDEO).first().waitFor({ state: 'visible', timeout: 30_000 });

        // assert - two videos
        await expectLabelsInsideSafeArea(caller, Portrait, true, 2);
        await caller.screenshot({ path: shot('3-both-grid') });
        await expectLabelsInsideSafeArea(caller, Portrait, false, 1);
        await caller.screenshot({ path: shot('4-both-speaker') });

        // act - the phone rotates: the notch moves to the left side
        await caller.setViewportSize(Landscape.viewport);
        const cdp = await caller.context().newCDPSession(caller);
        await cdp.send('Emulation.setSafeAreaInsetsOverride', { insets: Landscape.insets });

        // assert - landscape
        await expectLabelsInsideSafeArea(caller, Landscape, false, 1);
        await caller.screenshot({ path: shot('5-landscape-speaker') });
        await expectLabelsInsideSafeArea(caller, Landscape, true, 2);
        await caller.screenshot({ path: shot('6-landscape-grid') });
    }, 240_000);
});
