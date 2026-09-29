/**
 * Shared steps for e2e tests that need a live video session between two signed-in users: opening
 * the call chat, starting the recorder and the camera, driving the video panel, and hanging up.
 */

import * as path from 'path';
import { expect } from 'vitest';
import type { Page } from 'playwright';
import { BASE_URL, skipOnboarding, waitForChatReady, waitForEditor } from './helpers';

export const CALL_CHAT_URL = `${BASE_URL}/chat/the-actual-one`;
/** Real speech for the fake mic: silent recording idles out after 30s, and with no recorder
 *  left the session closes. Speech also registers the audio streams with the session. */
export const SPEECH_WAV = path.resolve('lib/data/test-audio-1.wav');

export async function openCallChat(page: Page) {
    await page.goto(CALL_CHAT_URL, { waitUntil: 'domcontentloaded' });
    await waitForChatReady(page);
    await skipOnboarding(page);

    const joinButton = page.locator('button:has-text("Join this chat")');
    if (await joinButton.isVisible({ timeout: 3000 }).catch(() => false)) {
        await joinButton.click();
        await page.waitForTimeout(1500);
    }
    await waitForEditor(page);
}

/** The video toggle shows only once the user records (or someone already streams video). */
export async function startRecording(page: Page) {
    // Recording is restored with the account's active chats, so a blind click can turn it off.
    const recordOn = page.locator('.chat-audio-panel .recorder-wrapper.record-on').first();
    if (!await recordOn.waitFor({ state: 'attached', timeout: 3_000 }).then(() => true, () => false))
        await page.locator('.chat-audio-panel .recorder-wrapper button').first().click();
    // record-on is the intent; applying-changes lasts until the recorder has really started
    await page.locator('.chat-audio-panel .recorder-wrapper.record-on:not(.applying-changes)').first()
        .waitFor({ state: 'attached', timeout: 30_000 });
}

export async function startCamera(page: Page) {
    await page.locator('.chat-audio-panel .video-wrapper button').first().click();
    // The first start asks through the join modal; a rejoin after a hang-up resumes without it
    const modal = page.locator('.modal').filter({ has: page.locator('.camera-preview-video') }).first();
    const preview = page.locator('.video-panel .video-streaming-preview').first();
    await expect.poll(async () => await modal.isVisible() || await preview.isVisible(), { timeout: 15_000 })
        .toBe(true);
    if (await modal.isVisible()) {
        const submit = modal.locator('.btn-modal.btn-primary').first();
        await expect.poll(async () => submit.isEnabled(), { timeout: 15_000 }).toBe(true);
        await submit.click();
    }
    await preview.waitFor({ state: 'visible', timeout: 20_000 });
}

export async function expandVideoPanel(page: Page) {
    // The expand button fades in (show-with-delay), and a click that lands before then is lost.
    const panel = page.locator('.video-panel').first();
    await expect.poll(async () => {
        if (!(await panel.getAttribute('class'))?.includes('expanded'))
            await panel.locator('.expand-btn').first().click({ timeout: 2_000 }).catch(() => { /* retried */ });
        return (await panel.getAttribute('class')) ?? '';
    }, { timeout: 20_000, interval: 1_000 }).toContain('expanded');
}

type MarkedElement = Element & { e2eMark?: boolean };

/** Stamps the panel's video and canvas elements, so a later check can tell a re-render (same
 *  elements, still stamped) from a recreated tile (new, unstamped elements). */
export async function markVideoElements(page: Page): Promise<number> {
    return page.evaluate(() => {
        const elements = [...document.querySelectorAll('.video-panel video, .video-panel canvas')];
        elements.forEach(e => { (e as MarkedElement).e2eMark = true; });
        return elements.length;
    });
}

/** How many of the panel's video and canvas elements carry the stamp, and how many don't. */
export async function countVideoElements(page: Page): Promise<{ marked: number; unmarked: number }> {
    return page.evaluate(() => {
        const elements = [...document.querySelectorAll('.video-panel video, .video-panel canvas')];
        const marked = elements.filter(e => (e as MarkedElement).e2eMark === true).length;
        return { marked, unmarked: elements.length - marked };
    });
}

export async function collapseVideoPanel(page: Page) {
    const panel = page.locator('.video-panel').first();
    if ((await panel.getAttribute('class'))?.includes('expanded'))
        await panel.locator('.expand-btn').first().click();
    await expect.poll(async () => (await panel.getAttribute('class')) ?? '', { timeout: 10_000 })
        .not.toContain('expanded');
}

export async function setGallery(page: Page, isOn: boolean) {
    const panel = page.locator('.video-panel').first();
    const isGallery = async () => ((await panel.getAttribute('class')) ?? '').includes('layout-equal');
    if (await isGallery() !== isOn)
        await panel.locator('.layout-toggle-btn').first().click();
    await expect.poll(isGallery, { timeout: 10_000 }).toBe(isOn);
}

export async function openCallTab(page: Page) {
    // A closed right panel stays in the DOM, parked just past the viewport's right edge, so Playwright
    // reports it visible - check where it is instead. Its toggle renders only while it's closed, and
    // right after the video panel collapses it can be mid-transition, so retry until the tab is on screen.
    await skipOnboarding(page);
    const width = page.viewportSize()?.width ?? 0;
    const callTab = page.locator('.chat-side-panel [data-tab-id="call"]').first();
    const toggle = page.locator('button:has(i.icon-layout)').first();
    await expect.poll(async () => {
        const x = (await callTab.boundingBox())?.x ?? width;
        if (x < width)
            return true;

        if (await toggle.isVisible())
            await toggle.click({ timeout: 2_000 }).catch(() => { /* retried */ });
        return false;
    }, { timeout: 20_000, interval: 1_000 }).toBe(true);
    await callTab.click();
}

// A failed test must not leave a live session in the shared chat: the next run would start
// with a session it didn't open, and the other account as its host.
export async function hangUpIfAny(page: Page | undefined) {
    if (!page)
        return;

    for (let i = 0; i < 2; i++) {
        await page.keyboard.press('Escape').catch(() => { /* ignore */ });
        await page.waitForTimeout(300);
    }
    const hangUp = page.locator('.video-panel .btn-video-panel.talking').first();
    if (await hangUp.isVisible({ timeout: 1_000 }).catch(() => false)) {
        await hangUp.click().catch(() => { /* ignore */ });
        await page.locator('.video-panel').first()
            .waitFor({ state: 'hidden', timeout: 15_000 }).catch(() => { /* ignore */ });
    }
    // Recording outlives the page (it's restored with the account's active chats), so stop it too.
    const recordOn = page.locator('.chat-audio-panel .recorder-wrapper.record-on').first();
    if (await recordOn.isVisible({ timeout: 1_000 }).catch(() => false))
        await page.locator('.chat-audio-panel .recorder-wrapper button').first().click().catch(() => { /* ignore */ });
}

/** Host first, so they own the session; resolves once the guest can react and the host sees the guest's tile. */
export async function startSession(host: Page, guest: Page) {
    // A session has to latch before a hand can go up, and it closes again if nobody is recording
    // yet when the cameras start. The speech WAV makes that rare; the retry covers the rest.
    for (let attempt = 1; ; attempt++) {
        await startRecording(host);
        await startRecording(guest);
        await startCamera(host);
        await startCamera(guest);
        const canReact = await guest.locator('.video-panel-footer .btn-react').first()
            .waitFor({ state: 'attached', timeout: 20_000 }).then(() => true, () => false);
        if (canReact)
            break;
        if (attempt === 3)
            throw new Error('The live session never latched: no React button after 3 attempts');

        console.log(`Live session did not latch (attempt ${attempt}), restarting both cameras`);
        await hangUpIfAny(guest);
        await hangUpIfAny(host);
        await host.waitForTimeout(3_000);
    }
    await host.locator('.video-panel .remote-video-container').first()
        .waitFor({ state: 'visible', timeout: 30_000 });
}
