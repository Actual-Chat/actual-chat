/**
 * E2E test: a camera that fails to start reports its error in the UI language (#4262).
 *
 * Alice's first video getUserMedia succeeds, so the join modal's preview settles and video starts;
 * every later one throws NotReadableError, which is what a camera held by another app produces.
 * Bob streams too, so the video panel outlives Alice stopping her camera, and her restart then
 * skips the modal and hits the failure. The recorder sends an error code to C#, which resolves it
 * against the catalog: her tile shows "Камера «…» недоступна" rather than English prose.
 *
 * Run: AC_E2E_SERVER=external npx vitest run tests/ts/e2e/video-recorder-error-localization.test.ts --config vitest.config.e2e.ts
 */
import * as path from 'path';
import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { BrowserContext, Page } from 'playwright';
import {
    BASE_URL, TEST_EMAIL, TEST_EMAIL_2, connectBrowser, newUserContext, screenshot, skipOnboarding,
    waitForChatReady, waitForEditor, withUILanguage, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e-4262', name);
const CHAT_URL = `${BASE_URL}/chat/the-actual-one`;
const SPEECH_WAV = path.resolve('lib/data/test-audio-1.wav');

const videoToggle = (page: Page) => page.locator('.chat-audio-panel .video-wrapper button').first();
const ownPreview = (page: Page) => page.locator('.video-panel .video-streaming-preview').first();

async function openChat(page: Page, language: string) {
    await page.goto(withUILanguage(CHAT_URL, language), { waitUntil: 'domcontentloaded' });
    await waitForChatReady(page);
    await skipOnboarding(page);
    const joinButton = page.locator('.join-footer button').first();
    if (await joinButton.isVisible({ timeout: 3000 }).catch(() => false)) {
        await joinButton.click();
        await page.waitForTimeout(1500);
    }
    await waitForEditor(page);
}

/** Tutorial bubbles overlay the footer; skipOnboarding matches their buttons by English text only. */
async function dismissBubbles(page: Page) {
    for (let i = 0; i < 10; i++) {
        const skip = page.locator('.bubble-buttons button').first();
        if (!await skip.isVisible().catch(() => false))
            return;

        await skip.click({ force: true }).catch(() => { /* retried */ });
        await page.waitForTimeout(300);
    }
}

async function startRecording(page: Page) {
    const recordOn = page.locator('.chat-audio-panel .recorder-wrapper.record-on').first();
    if (!await recordOn.waitFor({ state: 'attached', timeout: 3_000 }).then(() => true, () => false))
        await page.locator('.chat-audio-panel .recorder-wrapper button').first().click();
    await page.locator('.chat-audio-panel .recorder-wrapper.record-on:not(.applying-changes)').first()
        .waitFor({ state: 'attached', timeout: 30_000 });
}

async function stopRecording(page: Page) {
    const recordOn = page.locator('.chat-audio-panel .recorder-wrapper.record-on').first();
    if (await recordOn.isVisible().catch(() => false))
        await page.locator('.chat-audio-panel .recorder-wrapper button').first().click().catch(() => { /* ignore */ });
}

async function startCamera(page: Page) {
    await dismissBubbles(page);
    await videoToggle(page).click();
    const modal = page.locator('.modal').filter({ has: page.locator('.camera-preview-video') }).first();
    await expect.poll(async () => await modal.isVisible() || await ownPreview(page).isVisible(), { timeout: 15_000 })
        .toBe(true);
    if (await modal.isVisible()) {
        const submit = modal.locator('.btn-modal.btn-primary').first();
        await expect.poll(async () => submit.isEnabled(), { timeout: 15_000 }).toBe(true);
        await submit.click();
    }
    await ownPreview(page).waitFor({ state: 'visible', timeout: 20_000 });
}

async function stopVideo(page: Page) {
    await dismissBubbles(page);
    await videoToggle(page).click();
    await ownPreview(page).waitFor({ state: 'hidden', timeout: 15_000 });
}

describe('video recorder error localization', () => {
    let conn: BrowserConnection;
    let aliceCtx: BrowserContext;
    let bobCtx: BrowserContext;
    let alice: Page;
    let bob: Page;

    beforeAll(async () => {
        conn = await connectBrowser({ fakeAudioFile: SPEECH_WAV });
        ({ context: aliceCtx, page: alice } = await newUserContext(conn, TEST_EMAIL));
        ({ context: bobCtx, page: bob } = await newUserContext(conn, TEST_EMAIL_2));
        for (const ctx of [aliceCtx, bobCtx])
            await ctx.grantPermissions(['microphone', 'camera'], { origin: BASE_URL });
        await aliceCtx.addInitScript(() => {
            if (typeof MediaDevices === 'undefined')
                return;

            const original = MediaDevices.prototype.getUserMedia.bind(navigator.mediaDevices) as
                (constraints?: MediaStreamConstraints) => Promise<MediaStream>;
            let videoCalls = 0;
            MediaDevices.prototype.getUserMedia = (constraints?: MediaStreamConstraints): Promise<MediaStream> => {
                if (constraints?.video && ++videoCalls > 1)
                    return Promise.reject(new DOMException('Could not start video source', 'NotReadableError'));

                return original(constraints);
            };
        });
    }, 180_000);

    afterAll(async () => {
        for (const page of [alice, bob]) {
            await page.keyboard.press('Escape').catch(() => { /* ignore */ });
            if (await ownPreview(page).isVisible().catch(() => false))
                await stopVideo(page).catch(() => { /* ignore */ });
            await stopRecording(page);
            await page.goto('about:blank').catch(() => { /* ignore */ });
        }
        await aliceCtx.close().catch(() => { /* ignore */ });
        await bobCtx.close().catch(() => { /* ignore */ });
        if (conn.ownsBrowser) {
            await conn.context.close().catch(() => { /* ignore */ });
            await conn.browser.close().catch(() => { /* ignore */ });
        }
    }, 60_000);

    it('shows the camera-unavailable error in the UI language', async () => {
        try {
            await run();
        } catch (e) {
            await alice.screenshot({ path: shot('failure') }).catch(() => { /* ignore */ });
            throw e;
        }
    }, 150_000);

    async function run() {
        // arrange - Bob keeps the panel open; Alice's working first start is what lets her resume later
        await openChat(bob, 'en');
        await openChat(alice, 'ru');
        await startRecording(bob);
        await startRecording(alice);
        await startCamera(bob);
        await startCamera(alice);
        await alice.locator('.video-panel .remote-video-container').first()
            .waitFor({ state: 'visible', timeout: 30_000 });
        await stopVideo(alice);

        // act - a resume skips the join modal and goes straight to the recorder
        await videoToggle(alice).click();
        const error = alice.locator('.video-panel .video-streaming-preview .video-error').first();
        await error.waitFor({ state: 'visible', timeout: 30_000 });
        await alice.screenshot({ path: shot('camera-unavailable-ru') });
        const text = (await error.innerText()).trim();

        // assert
        expect(text).toContain('недоступна');
        expect(text.toLowerCase()).not.toContain('unavailable');
    }
});
