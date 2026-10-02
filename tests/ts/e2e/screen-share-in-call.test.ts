/**
 * E2E test: a screen shared in a call reaches the other participant (#5033).
 *
 * Two users are on a peer call on a wide screen, neither with a camera on. The caller shares the
 * screen: the callee must get the video panel with the shared screen playing in it.
 *
 * The headless browser has no hardware encoder, so the sharer ends up on the software VP9 rung of
 * the encoder ladder, which used to be configured as hardware and so never started. It gets there
 * two ways:
 *   - straight away, as the best rung the device has;
 *   - after a hardware codec it started on failed, which the page is made to believe it has.
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
import {
    OWN_VIDEO, REMOTE_VIDEO, WIDE, hangUpIfAny, isShown, signInBoth, signOutBoth, startPeerCall, type Users,
} from './peer-call';

const SHOTS_DIR = path.join(process.cwd(), 'tmp', 'e2e-screen-share');
fs.mkdirSync(SHOTS_DIR, { recursive: true });
const shot = (name: string) => path.join(SHOTS_DIR, `${name}.png`);

const SCREEN_SHARE_START = '.chat-audio-panel .screencast-wrapper.screen-share-start button';
const ALREADY_SHARING_OK = '[id^="Modal-ScreenCastAlreadyActiveModal"] .btn-primary';

/** Resolves once the own share is up. A share that just ended stays listed on the server for a few
 *  seconds, and the next one is refused as "already sharing" until it's gone - hence the retry. */
async function shareScreen(page: Page) {
    await page.locator(SCREEN_SHARE_START).first().waitFor({ state: 'visible', timeout: 30_000 });
    await expect.poll(async () => {
        if (await isShown(page, OWN_VIDEO))
            return true;

        const selector = await isShown(page, ALREADY_SHARING_OK) ? ALREADY_SHARING_OK : SCREEN_SHARE_START;
        await page.locator(selector).first().click({ timeout: 2_000 }).catch(() => { /* retried */ });
        return false;
    }, { timeout: 45_000, interval: 1_000 }).toBe(true);
}

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
        await caller.locator(SCREEN_SHARE_START).first().waitFor({ state: 'visible', timeout: 30_000 });
        await caller.screenshot({ path: shot('1-caller-on-call') });
        await callee.screenshot({ path: shot('1-callee-on-call') });

        // act - the caller shares the screen
        await shareScreen(caller);
        await caller.screenshot({ path: shot('2-caller-sharing') });

        // assert - the callee sees it playing
        await callee.locator(REMOTE_VIDEO).first().waitFor({ state: 'visible', timeout: 30_000 });
        await expect.poll(() => getRemoteFrameSize(callee), { timeout: 30_000 }).toBeGreaterThan(0);
        await callee.waitForTimeout(1_500);
        await callee.screenshot({ path: shot('2-callee-sees-shared-screen') });
    }, 180_000);

    it('recovers a screen share whose hardware encoder fails to start', async () => {
        // arrange - the caller's page takes hardware AV1 for supported; the recorder worker, where
        // the real encoder is created, is untouched, so there it fails to start
        const { caller, callee } = users;
        const callerLog: string[] = [];
        caller.on('console', m => callerLog.push(m.text()));
        await caller.addInitScript(fakeHardwareAv1Support);
        await startPeerCall(caller, callee);

        // act - the caller shares the screen
        await shareScreen(caller);

        // assert - the share started on AV1, lost it, and reached the callee on the codec it fell back to
        const hasAv1Failed = () => callerLog.some(t => t.includes('encoder init failure for codec=av01'));
        await expect.poll(hasAv1Failed, { timeout: 30_000 }).toBe(true);
        await callee.locator(REMOTE_VIDEO).first().waitFor({ state: 'visible', timeout: 30_000 });
        await expect.poll(() => getRemoteFrameSize(callee), { timeout: 30_000 }).toBeGreaterThan(0);
        await callee.waitForTimeout(1_500);
        await caller.screenshot({ path: shot('3-caller-sharing-after-encoder-failure') });
        await callee.screenshot({ path: shot('3-callee-sees-recovered-share') });
    }, 180_000);
});

/** Runs in the page: reports hardware AV1 as supported and lets the page's own probes of it
 *  pass on a software VP9 encoder. */
function fakeHardwareAv1Support() {
    interface EncoderConfig { codec: string; hardwareAcceleration?: string }
    interface EncoderClass {
        isConfigSupported(config: EncoderConfig): Promise<{ supported?: boolean; config?: EncoderConfig }>;
        prototype: { configure(config: EncoderConfig): void };
    }

    const encoderClass = (globalThis as unknown as { VideoEncoder: EncoderClass }).VideoEncoder;
    const isFakeHardware = (config: EncoderConfig) =>
        config.codec.startsWith('av01') && config.hardwareAcceleration === 'prefer-hardware';
    // eslint-disable-next-line @typescript-eslint/unbound-method
    const { isConfigSupported } = encoderClass;
    encoderClass.isConfigSupported = config => isFakeHardware(config)
        ? Promise.resolve({ supported: true, config })
        : isConfigSupported(config);
    // eslint-disable-next-line @typescript-eslint/unbound-method
    const configure = encoderClass.prototype.configure;
    encoderClass.prototype.configure = function (config) {
        const actualConfig = isFakeHardware(config)
            ? { ...config, codec: 'vp09.00.31.08', hardwareAcceleration: 'prefer-software' }
            : config;
        configure.call(this, actualConfig);
    };
}
