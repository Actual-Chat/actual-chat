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
 * The shared screen must also be shown whole to both of them, not cropped into a video frame's
 * shape or to fill the tile, as a camera is (#5043).
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
import { expandVideoPanel } from './video-call';

const SHOTS_DIR = path.join(process.cwd(), 'tmp', 'e2e-screen-share');
fs.mkdirSync(SHOTS_DIR, { recursive: true });
const shot = (name: string) => path.join(SHOTS_DIR, `${name}.png`);

/** Landscape like the shared screen, but far from its shape: a camera would be cropped to fill it. */
const ULTRAWIDE = { width: 1800, height: 600 };
/** The shape of the screen the caller shares: 16:10, as on a laptop, not the 16:9 of a video frame. */
const SCREEN_ASPECT = 1.6;
const RESIZED_SCREEN = { width: 2000, height: 1000 };

interface FakeScreenWindow {
    e2eResizeScreen(size: { width: number; height: number }): void;
}
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

interface ShownFrame {
    /** Width to height of the frame the tile plays. */
    aspect: number;
    /** The part of that frame the tile leaves out, 0 when all of it is on screen. */
    croppedFraction: number;
}

function getShownFrame(page: Page, tileSelector: string): Promise<ShownFrame | null> {
    return page.evaluate(selector => {
        const tile = document.querySelector(selector);
        const surfaces = [...tile?.querySelectorAll<HTMLElement>('video, canvas:not(.remote-video-bg)') ?? []];
        const surface = surfaces.find(e => getComputedStyle(e).display !== 'none');
        if (!surface)
            return null;

        const [frameW, frameH] = surface instanceof HTMLVideoElement
            ? [surface.videoWidth, surface.videoHeight]
            : [(surface as HTMLCanvasElement).width, (surface as HTMLCanvasElement).height];
        if (frameW <= 0 || frameH <= 0)
            return null;

        const a = frameW * surface.clientHeight;
        const b = frameH * surface.clientWidth;
        const isCovering = getComputedStyle(surface).objectFit === 'cover';
        return { aspect: frameW / frameH, croppedFraction: isCovering ? 1 - Math.min(a, b) / Math.max(a, b) : 0 };
    }, tileSelector).catch(() => null);
}

describe('screen share in a call, wide screen', () => {
    let users: Users;

    beforeAll(async () => {
        users = await signInBoth(WIDE);
        await users.caller.addInitScript(fakeNonWidescreenDisplay);
    }, 180_000);

    afterEach(async () => {
        await users.callee.setViewportSize(WIDE);
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

        // act - the callee expands the video in a window much wider than the shared screen
        await callee.setViewportSize(ULTRAWIDE);
        await expandVideoPanel(callee);
        await callee.waitForTimeout(1_500);
        await callee.screenshot({ path: shot('2-callee-sees-shared-screen-expanded') });
        await caller.screenshot({ path: shot('2-caller-sees-own-screen') });

        // assert - neither side has lost any of the shared screen, to the encoder or to the tile
        const shared = await getShownFrame(callee, REMOTE_VIDEO);
        const own = await getShownFrame(caller, OWN_VIDEO);
        expect(shared?.aspect, 'the shared screen must keep its shape').toBeCloseTo(SCREEN_ASPECT, 1);
        expect(shared?.croppedFraction, 'the viewer must see the whole screen').toBeLessThan(0.01);
        expect(own?.aspect, 'the own preview must keep the shape of the screen').toBeCloseTo(SCREEN_ASPECT, 1);
        expect(own?.croppedFraction, 'the sharer must see the whole screen').toBeLessThan(0.01);

        // act - the shared surface changes shape, as a tab or a window does when it's resized
        await caller.evaluate(size => (window as unknown as FakeScreenWindow).e2eResizeScreen(size), RESIZED_SCREEN);

        // assert - both sides follow it, still whole
        const resizedAspect = RESIZED_SCREEN.width / RESIZED_SCREEN.height;
        await expect.poll(async () => (await getShownFrame(callee, REMOTE_VIDEO))?.aspect, { timeout: 30_000 })
            .toBeCloseTo(resizedAspect, 1);
        await expect.poll(async () => (await getShownFrame(caller, OWN_VIDEO))?.aspect, { timeout: 30_000 })
            .toBeCloseTo(resizedAspect, 1);
        await callee.waitForTimeout(1_500);
        await callee.screenshot({ path: shot('2-callee-sees-resized-screen') });
        await caller.screenshot({ path: shot('2-caller-sees-own-resized-screen') });
        expect((await getShownFrame(callee, REMOTE_VIDEO))?.croppedFraction).toBeLessThan(0.01);
        expect((await getShownFrame(caller, OWN_VIDEO))?.croppedFraction).toBeLessThan(0.01);
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

/** Runs in the page: the screen it shares is a 16:10 picture with a mark in every corner, so a
 *  crop anywhere on the way is visible, and it can be resized like a shared tab or window.
 *  The headless browser's own fake screen is 16:9 and stays that way. */
function fakeNonWidescreenDisplay() {
    const mark = 120;
    // No media devices on the blank page the run ends on
    if (!('mediaDevices' in navigator))
        return;

    const canvas = document.createElement('canvas');
    canvas.width = 1600;
    canvas.height = 1000;
    let frameIndex = 0;
    const draw = () => {
        const { width, height } = canvas;
        const context = canvas.getContext('2d')!;
        context.fillStyle = '#123a5c';
        context.fillRect(0, 0, width, height);
        context.fillStyle = '#ff9d00';
        for (const [x, y] of [[0, 0], [width - mark, 0], [0, height - mark], [width - mark, height - mark]])
            context.fillRect(x, y, mark, mark);
        context.fillStyle = '#ffffff';
        context.font = '48px sans-serif';
        context.textAlign = 'center';
        context.fillText('TOP EDGE - menu bar', width / 2, 56);
        context.fillText('BOTTOM EDGE - dock', width / 2, height - 24);
        context.fillText(`${width}x${height}, frame ${frameIndex++}`, width / 2, height / 2);
    };
    setInterval(draw, 100);
    (window as unknown as FakeScreenWindow).e2eResizeScreen = size => {
        canvas.width = size.width;
        canvas.height = size.height;
    };
    navigator.mediaDevices.getDisplayMedia = () => {
        draw();
        return Promise.resolve(canvas.captureStream(15));
    };
}

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
