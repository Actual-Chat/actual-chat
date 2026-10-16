/**
 * E2E test: picking the camera that is already selected leaves the video modal's preview as it is (#5385).
 *
 * The preview went to loading on every pick and waited for the camera pipeline to restart; for the
 * camera already in use nothing restarts, so the spinner never went away, and in the join preview
 * the Start video button stayed disabled. Checked in both places the modal opens from:
 *   - the join preview, before the camera is on;
 *   - the video settings, with the camera live.
 * Wide screen only: there the camera list opens from the camera pill's chevron; a narrow screen
 * opens it with a long press on the switch-camera button.
 *
 * Screenshots go to tmp/e2e-camera-repick/.
 *
 * Prerequisites: a running server (server-loop / run-watch).
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/camera-repick.test.ts --config vitest.config.e2e.ts
 */

import * as fs from 'fs';
import * as path from 'path';
import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import type { BrowserContext, Page } from 'playwright';
import { TEST_EMAIL, connectBrowser, type BrowserConnection } from './helpers';
import { OWN_VIDEO, WIDE, isShown, newUserPage } from './peer-call';
import { SPEECH_WAV, hangUpIfAny, openCallChat, startRecording } from './video-call';

const SHOTS_DIR = path.join(process.cwd(), 'tmp', 'e2e-camera-repick');
fs.mkdirSync(SHOTS_DIR, { recursive: true });
const shot = (name: string) => path.join(SHOTS_DIR, `${name}.png`);

const MODAL = '.join-video-call-modal';
const PREVIEW_LOADING = `${MODAL} .c-video.camera-starting`;
// A same-camera pick changes nothing, so the preview has no reason to go to loading even briefly;
// this is how long a regression gets to show the spinner before the check.
const SETTLE_MS = 3_000;

describe('picking the selected camera again', () => {
    let conn: BrowserConnection;
    let context: BrowserContext;
    let page: Page;

    beforeAll(async () => {
        conn = await connectBrowser({ fakeAudioFile: SPEECH_WAV });
        ({ context, page } = await newUserPage(conn, TEST_EMAIL, WIDE));
        await openCallChat(page);
        // The video toggle shows only while recording
        await startRecording(page);
    }, 180_000);

    afterEach(async () => {
        // A failed test leaves the modal open, and the next one starts from the chat
        const modal = page.locator(MODAL).first();
        if (await modal.isVisible().catch(() => false)) {
            await page.keyboard.press('Escape');
            await modal.waitFor({ state: 'hidden', timeout: 10_000 }).catch(() => { /* ignore */ });
        }
    }, 30_000);

    afterAll(async () => {
        await hangUpIfAny(page);
        // Unload before closing, so the circuit doesn't keep recording as this account into the next file
        await page.goto('about:blank').catch(() => { /* ignore */ });
        await context.close().catch(() => { /* ignore */ });
        if (conn.ownsBrowser) {
            await conn.context.close().catch(() => { /* ignore */ });
            await conn.browser.close().catch(() => { /* ignore */ });
        }
    }, 60_000);

    it('keeps the join preview live and Start video enabled', async () => {
        // arrange - the join preview is up with its camera live
        await page.locator('.chat-audio-panel .video-wrapper button').first().click();
        await waitForPreviewLive(page);
        const modal = page.locator(MODAL).first();
        const submit = modal.locator('.btn-modal.btn-primary').first();
        await expect.poll(() => submit.isEnabled(), { timeout: 15_000 }).toBe(true);
        await page.screenshot({ path: shot('join-1-preview-live') });

        // act
        await pickSelectedCamera(page);
        await page.waitForTimeout(SETTLE_MS);

        // assert
        await page.screenshot({ path: shot('join-2-after-repick') });
        expect(await isShown(page, PREVIEW_LOADING), 'the preview does not go back to loading').toBe(false);
        expect(await submit.isEnabled(), 'Start video stays enabled').toBe(true);

        await modal.locator('.btn-modal:not(.btn-primary)').first().click();
        await modal.waitFor({ state: 'hidden', timeout: 10_000 });
    }, 120_000);

    it('keeps the video settings preview live', async () => {
        // arrange - the camera is on, and its settings are open from the video panel's menu
        await page.locator('.chat-audio-panel .video-wrapper button').first().click();
        await waitForPreviewLive(page);
        const modal = page.locator(MODAL).first();
        const submit = modal.locator('.btn-modal.btn-primary').first();
        await expect.poll(() => submit.isEnabled(), { timeout: 15_000 }).toBe(true);
        await submit.click();
        await modal.waitFor({ state: 'hidden', timeout: 20_000 });
        await page.locator(OWN_VIDEO).first().waitFor({ state: 'visible', timeout: 30_000 });
        await page.locator('[data-menu*="VideoPanelMenu"]').first().click();
        await page.locator('.ac-menu-item:has-text("Video settings")').first().click();
        await waitForPreviewLive(page);
        await page.screenshot({ path: shot('settings-1-preview-live') });

        // act
        await pickSelectedCamera(page);
        await page.waitForTimeout(SETTLE_MS);

        // assert
        await page.screenshot({ path: shot('settings-2-after-repick') });
        expect(await isShown(page, PREVIEW_LOADING), 'the preview does not go back to loading').toBe(false);

        await modal.locator('.btn-modal.btn-primary').first().click();
        await modal.waitFor({ state: 'hidden', timeout: 10_000 });
    }, 120_000);
});

/** The preview is live once its first frame has rendered and the loading state has gone. */
async function waitForPreviewLive(page: Page) {
    await page.locator(`${MODAL} .video-frame.has-video`).first().waitFor({ state: 'visible', timeout: 30_000 });
    await page.locator(PREVIEW_LOADING).first().waitFor({ state: 'detached', timeout: 30_000 });
}

async function pickSelectedCamera(page: Page) {
    await page.locator(`${MODAL} .pill-chevron`).first().click();
    const selected = page.locator('.camera-menu .ac-menu-item.selected').first();
    await selected.waitFor({ state: 'visible', timeout: 10_000 });
    await selected.click();
}
