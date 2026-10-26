/**
 * E2E test: a phone buzzes when the other side of a call turns their camera on (#5571).
 *
 * Mid-call video moves the sound from the earpiece to the speaker, and a phone held to the ear has
 * its screen off, so the switch used to be the only sign of the video. Two users are on an
 * audio-only call at a phone-sized viewport; the callee turns the camera on. The caller's phone
 * vibrates and plays a sound, the callee's - whose own camera it is - doesn't.
 *
 * `navigator.vibrate` is stubbed on both pages to record its calls; the tune's pattern is
 * [200, 120, 200], so it shows up as two 200 ms vibrations. The sound shows up as a request for
 * its file, fetched when the tune first plays.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch), locally - see peer-call.ts.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/call-video-started-notice.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import type { Page } from 'playwright';
import {
    CALL_CONTROLS, NARROW, REMOTE_VIDEO, hangUpIfAny, signInBoth, signOutBoth, startPeerCall, type Users,
} from './peer-call';

const VIDEO_STARTED_VIBRATION_MS = 200;
const VIDEO_STARTED_SOUND = '/sounds/call-video-started.';

type VibrationLog = Window & { e2eVibrations?: number[] };

describe('notice of video started mid-call', () => {
    let users: Users | undefined;

    beforeAll(async () => {
        users = await signInBoth(NARROW);
        // Takes effect on the next navigation, which startPeerCall makes on both pages
        for (const page of [users.caller, users.callee])
            await page.addInitScript(() => {
                const w = window as VibrationLog;
                w.e2eVibrations = [];
                navigator.vibrate = (pattern: number | Iterable<number>) => {
                    w.e2eVibrations!.push(...(typeof pattern === 'number' ? [pattern] : pattern));
                    return true;
                };
            });
    }, 180_000);

    afterEach(async () => {
        await hangUpIfAny(users?.caller);
        await hangUpIfAny(users?.callee);
    }, 60_000);

    afterAll(async () => {
        await signOutBoth(users);
    });

    it('notifies the one who sees the video, not the one who sends it', async () => {
        // arrange
        const { caller, callee } = users!;
        const countVibrations = (page: Page) => page.evaluate(
            ms => ((window as VibrationLog).e2eVibrations ?? []).filter(x => x === ms).length,
            VIDEO_STARTED_VIBRATION_MS);
        await startPeerCall(caller, callee);
        for (const page of [caller, callee])
            await page.locator(CALL_CONTROLS).first().waitFor({ state: 'visible', timeout: 30_000 });
        const callerCountBefore = await countVibrations(caller);
        const soundRequests = { caller: 0, callee: 0 };
        caller.on('request', r => { if (r.url().includes(VIDEO_STARTED_SOUND)) soundRequests.caller++; });
        callee.on('request', r => { if (r.url().includes(VIDEO_STARTED_SOUND)) soundRequests.callee++; });

        // act
        await callee.locator(`${CALL_CONTROLS} .btn-video-toggle`).first().click();

        // assert
        await caller.locator(REMOTE_VIDEO).first().waitFor({ state: 'visible', timeout: 30_000 });
        await expect.poll(() => countVibrations(caller), { timeout: 10_000 })
            .toBe(callerCountBefore + 2);
        // Long enough for a tune that was going to play to have played
        await callee.waitForTimeout(2_000);
        await expect.poll(() => soundRequests.caller, { timeout: 10_000 }).toBeGreaterThan(0);
        const calleeCount = await countVibrations(callee);
        expect(calleeCount, 'own camera needs no notice').toBe(0);
        expect(soundRequests.callee, 'own camera needs no sound').toBe(0);
    }, 180_000);
});
