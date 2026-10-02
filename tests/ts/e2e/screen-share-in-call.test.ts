/**
 * E2E test: a screen shared in a call reaches the other participant (#5033).
 *
 * Two users are on a peer call on a wide screen, neither with a camera on. The caller shares the
 * screen: the callee must get the video panel with the shared screen playing in it.
 *
 * The headless browser has no hardware encoder, so the sharer lands on the software VP9 rung of the
 * encoder ladder - the one a screencast used to configure as hardware, and so never started.
 *
 * Screenshots go to tmp/e2e-screen-share/.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch), locally - see peer-call.ts.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/screen-share-in-call.test.ts --config vitest.config.e2e.ts
 */

import * as fs from 'fs';
import * as path from 'path';
import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import type { Page } from 'playwright';
import { OWN_VIDEO, REMOTE_VIDEO, WIDE, hangUpIfAny, signInBoth, signOutBoth, startPeerCall, type Users }
    from './peer-call';

const SHOTS_DIR = path.join(process.cwd(), 'tmp', 'e2e-screen-share');
fs.mkdirSync(SHOTS_DIR, { recursive: true });
const shot = (name: string) => path.join(SHOTS_DIR, `${name}.png`);

const SCREEN_SHARE_START = '.chat-audio-panel .screencast-wrapper.screen-share-start button';

/** Decoded frames the remote tile has put on screen: a tile can be up with nothing playing in it. */
function getRemoteFrameSize(page: Page): Promise<number> {
    return page.evaluate(selector => {
        const tile = document.querySelector(selector);
        const video = tile?.querySelector('video');
        if (video && video.videoWidth > 0 && !video.paused)
            return video.videoWidth * video.videoHeight;

        const canvas = tile?.querySelector('canvas');
        return canvas ? canvas.width * canvas.height : 0;
    }, REMOTE_VIDEO).catch(() => 0);
}

describe('screen share in a call, wide screen', () => {
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

    it('shows the shared screen to the other participant', async () => {
        // arrange - both on the call, no video yet
        const { caller, callee } = users;
        await startPeerCall(caller, callee);
        const shareButton = caller.locator(SCREEN_SHARE_START).first();
        await shareButton.waitFor({ state: 'visible', timeout: 30_000 });
        await caller.screenshot({ path: shot('1-caller-on-call') });
        await callee.screenshot({ path: shot('1-callee-on-call') });

        // act - the caller shares the screen
        await shareButton.click();
        await caller.locator(OWN_VIDEO).first().waitFor({ state: 'visible', timeout: 30_000 });
        await caller.screenshot({ path: shot('2-caller-sharing') });

        // assert - the callee sees it playing
        await callee.locator(REMOTE_VIDEO).first().waitFor({ state: 'visible', timeout: 30_000 });
        await expect.poll(() => getRemoteFrameSize(callee), { timeout: 30_000 }).toBeGreaterThan(0);
        await callee.waitForTimeout(1_500);
        await callee.screenshot({ path: shot('2-callee-sees-shared-screen') });
    }, 180_000);
});
