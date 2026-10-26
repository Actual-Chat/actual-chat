/**
 * E2E test: a raised hand in an audio-only call (#5521).
 *
 * Two users are on a call in their peer chat, at a phone-sized viewport, with no camera on - so no
 * tile carries the hand. The callee raises a hand, and the caller sees a blue counter under the
 * call timer, hears the hand go up, and keeps the counter on the island once the call collapses.
 *
 * Transcription is turned off first: a hand lowers itself once its owner says a few words (#5037),
 * and the fake mic never stops talking.
 *
 * Screenshots go to tmp/e2e-raise-hand-audio-call/.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch), locally: calls are incomplete UI, which the test
 *   turns on for both accounts - test agents are admins only on a local server.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/raise-hand-audio-call.test.ts --config vitest.config.e2e.ts
 */

import * as fs from 'fs';
import * as path from 'path';
import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import {
    CALL_COLLAPSE, CALL_CONTROLS, CALL_SCREEN, NARROW, hangUpIfAny, signInBoth, signOutBoth, startPeerCall,
    type Users,
} from './peer-call';
import { openReactionsMenu } from './video-call';

const SHOTS_DIR = path.join(process.cwd(), 'tmp', 'e2e-raise-hand-audio-call');
fs.mkdirSync(SHOTS_DIR, { recursive: true });
const shot = (name: string) => path.join(SHOTS_DIR, `${name}.png`);

const TRANSCRIPTION_TOGGLE = `${CALL_CONTROLS} .btn-transcription`;
const BODY_COUNTER = `${CALL_SCREEN} .call-screen-body .raised-hands-counter`;
const ISLAND_COUNTER = '.collapsed-call-view .raised-hands-counter';

const isHandRaisedSound = (url: string) => url.includes('/sounds/hand-raised.');

describe('raise hand in an audio-only call', () => {
    let users: Users | undefined;

    beforeAll(async () => {
        users = await signInBoth(NARROW);
    }, 180_000);

    afterEach(async () => {
        await hangUpIfAny(users?.caller);
        await hangUpIfAny(users?.callee);
    }, 60_000);

    afterAll(async () => {
        await signOutBoth(users);
    });

    it('shows a raised hand as a counter on the call screen and on the island', async () => {
        // arrange
        const { caller, callee } = users!;
        await startPeerCall(caller, callee);
        await callee.locator(`${TRANSCRIPTION_TOGGLE}.on`).first().waitFor({ state: 'visible', timeout: 15_000 });
        await callee.locator(TRANSCRIPTION_TOGGLE).first().click();
        for (const page of [caller, callee])
            await page.locator(`${TRANSCRIPTION_TOGGLE}:not(.on)`).first()
                .waitFor({ state: 'visible', timeout: 15_000 });
        const calleeSoundUrls: string[] = [];
        callee.on('request', request => {
            if (isHandRaisedSound(request.url()))
                calleeSoundUrls.push(request.url());
        });
        const whenCallerHearsHand = caller.waitForRequest(r => isHandRaisedSound(r.url()), { timeout: 15_000 });

        // act
        const menu = await openReactionsMenu(callee, 20_000);
        expect(menu, 'the call offers reactions').not.toBeNull();
        await menu!.locator('.ac-menu-item:has(.icon-hand)').first().click();

        // assert - the call screen shows the hand under the timer, blue and waving
        const bodyCounter = caller.locator(BODY_COUNTER).first();
        await bodyCounter.waitFor({ state: 'visible', timeout: 15_000 });
        const bodyCount = await bodyCounter.locator('.c-count').innerText();
        const background = await bodyCounter.evaluate(el => getComputedStyle(el).backgroundColor);
        const animation = await bodyCounter.locator('i').evaluate(el => getComputedStyle(el).animationName);
        expect(bodyCount.trim()).toBe('1');
        expect(background).toBe('rgb(51, 149, 255)');
        expect(animation).toBe('hand-wave');
        await caller.screenshot({ path: shot('1-call-screen') });

        // assert - the caller hears the hand go up; the callee, whose hand it is, doesn't
        await whenCallerHearsHand;
        expect(calleeSoundUrls).toEqual([]);

        // act - the caller collapses the call into its island
        await caller.locator(CALL_COLLAPSE).first().click();

        // assert - the island keeps the counter
        const islandCounter = caller.locator(ISLAND_COUNTER).first();
        await islandCounter.waitFor({ state: 'visible', timeout: 15_000 });
        const islandCount = await islandCounter.locator('.c-count').innerText();
        expect(islandCount.trim()).toBe('1');
        await caller.screenshot({ path: shot('2-island') });
    }, 240_000);
});
