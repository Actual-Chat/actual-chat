/**
 * Shared steps for e2e tests that need a live video session between two signed-in users: opening
 * the call chat, starting the recorder and the camera, driving the call screen's video, and hanging up.
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

/** Mutes the mic and leaves the camera on, so the user stays in the session without saying a word. */
export async function stopRecording(page: Page) {
    const recordOn = page.locator('.chat-audio-panel .recorder-wrapper.record-on').first();
    if (await recordOn.waitFor({ state: 'attached', timeout: 3_000 }).then(() => true, () => false))
        await page.locator('.chat-audio-panel .recorder-wrapper button').first().click();
    await page.locator('.chat-audio-panel .recorder-wrapper:not(.record-on):not(.applying-changes)').first()
        .waitFor({ state: 'attached', timeout: 30_000 });
}

export async function startCamera(page: Page) {
    await page.locator('.chat-audio-panel .video-wrapper button').first().click();
    // The first start asks through the join modal; a rejoin after a hang-up resumes without it
    const modal = page.locator('.modal').filter({ has: page.locator('.camera-preview-video') }).first();
    const preview = page.locator('.call-screen .video-streaming-preview').first();
    await expect.poll(async () => await modal.isVisible() || await preview.isVisible(), { timeout: 15_000 })
        .toBe(true);
    if (await modal.isVisible()) {
        const submit = modal.locator('.btn-modal.btn-primary').first();
        await expect.poll(async () => submit.isEnabled(), { timeout: 15_000 }).toBe(true);
        await submit.click();
    }
    await preview.waitFor({ state: 'visible', timeout: 20_000 }).catch(async (e: unknown) => {
        const details = await impl().catch((de: unknown) => `  failed: ${String(de)}`);
        console.log(`CAMERA PREVIEW HIDDEN on ${page.url()}\n${details}`);
        throw e;
    });

    // Playwright counts an element hidden at zero size or under display:none / visibility:hidden,
    // its own or an ancestor's; this shows which, and whether the camera's frames got there at all
    function impl(): Promise<string> {
        return page.evaluate(() => {
            const element = document.querySelector('.call-screen .video-streaming-preview');
            if (!element)
                return '  no .call-screen .video-streaming-preview';

            const describe = (e: Element) => {
                const style = getComputedStyle(e);
                const rect = e.getBoundingClientRect();
                return `${e.tagName.toLowerCase()}.${[...e.classList].join('.')} `
                    + `${Math.round(rect.width)}x${Math.round(rect.height)} display:${style.display} `
                    + `visibility:${style.visibility} opacity:${style.opacity}`;
            };
            const lines = ['  preview and its ancestors:'];
            for (let e: Element | null = element; e && e !== document.body; e = e.parentElement)
                lines.push(`    ${describe(e)}`);
            lines.push(`  attributes: ${[...element.attributes].map(a => `${a.name}="${a.value}"`).join(' ')}`);
            lines.push('  surfaces:');
            for (const surface of element.querySelectorAll('video, canvas')) {
                let line = `    ${describe(surface)}`;
                if (surface instanceof HTMLVideoElement) {
                    const tracks = surface.srcObject instanceof MediaStream ? surface.srcObject.getVideoTracks() : [];
                    const trackStates = tracks
                        .map(t => `${t.readyState}${t.muted ? ' muted' : ''}${t.enabled ? '' : ' disabled'}`);
                    line += ` readyState:${surface.readyState} frame:${surface.videoWidth}x${surface.videoHeight}`
                        + ` paused:${surface.paused} tracks:[${trackStates.join(', ')}]`;
                }
                lines.push(line);
            }
            lines.push(`  join modal open: ${document.querySelector('.modal .camera-preview-video') !== null}`);
            return lines.join('\n');
        });
    }
}

export async function expandVideoPanel(page: Page) {
    // The expand button shows only once the opening animation ends (first-time-open); a click before then is lost.
    const panel = page.locator('.call-screen').first();
    await expect.poll(async () => {
        if (!(await panel.getAttribute('class'))?.includes('expanded'))
            await panel.locator('.btn-expand').first().click({ timeout: 2_000 }).catch(() => { /* retried */ });
        return (await panel.getAttribute('class')) ?? '';
    }, { timeout: 20_000, interval: 1_000 }).toContain('expanded');
}

type MarkedElement = Element & { e2eMark?: boolean };

/** Stamps the panel's video and canvas elements, so a later check can tell a re-render (same
 *  elements, still stamped) from a recreated tile (new, unstamped elements). */
export async function markVideoElements(page: Page): Promise<number> {
    return page.evaluate(() => {
        const elements = [...document.querySelectorAll('.call-screen video, .call-screen canvas')];
        elements.forEach(e => { (e as MarkedElement).e2eMark = true; });
        return elements.length;
    });
}

/** How many of the panel's video and canvas elements carry the stamp, and how many don't. */
export async function countVideoElements(page: Page): Promise<{ marked: number; unmarked: number }> {
    return page.evaluate(() => {
        const elements = [...document.querySelectorAll('.call-screen video, .call-screen canvas')];
        const marked = elements.filter(e => (e as MarkedElement).e2eMark === true).length;
        return { marked, unmarked: elements.length - marked };
    });
}

export async function collapseVideoPanel(page: Page) {
    const panel = page.locator('.call-screen').first();
    if ((await panel.getAttribute('class'))?.includes('expanded'))
        await panel.locator('.btn-expand').first().click();
    await expect.poll(async () => (await panel.getAttribute('class')) ?? '', { timeout: 10_000 })
        .not.toContain('expanded');
}

export async function setGallery(page: Page, isOn: boolean) {
    const panel = page.locator('.call-screen').first();
    const isGallery = async () => ((await panel.getAttribute('class')) ?? '').includes('layout-equal');
    if (await isGallery() !== isOn)
        await panel.locator('.btn-layout-toggle').first().click();
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

    // An expanded call screen gives its Back step away only after it has hidden, and a page.goto
    // that this history.back() lands in is aborted - so the caller's next navigation waits for it.
    const whenWentBack = page.evaluate(() => new Promise<void>(resolve => {
        window.addEventListener('popstate', () => resolve(), { once: true });
    })).catch(() => { /* ignore */ });
    for (let i = 0; i < 2; i++) {
        await page.keyboard.press('Escape').catch(() => { /* ignore */ });
        await page.waitForTimeout(300);
    }
    const hangUp = page.locator('.call-screen .btn-hang-up').first();
    if (await hangUp.isVisible({ timeout: 1_000 }).catch(() => false)) {
        await hangUp.click().catch(() => { /* ignore */ });
        await page.locator('.call-screen').first()
            .waitFor({ state: 'hidden', timeout: 15_000 }).catch(() => { /* ignore */ });
        // A screen that wasn't expanded has no step to give away
        await Promise.race([whenWentBack, page.waitForTimeout(1_000)]);
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
        // The React button is one of the call screen's controls, so it takes the full-screen mode to see it
        await expandVideoPanel(guest);
        const canReact = await guest.locator('.call-screen-footer .btn-react').first()
            .waitFor({ state: 'attached', timeout: 20_000 }).then(() => true, () => false);
        await collapseVideoPanel(guest);
        if (canReact)
            break;
        if (attempt === 3)
            throw new Error('The live session never latched: no React button after 3 attempts');

        console.log(`Live session did not latch (attempt ${attempt}), restarting both cameras`);
        await hangUpIfAny(guest);
        await hangUpIfAny(host);
        await host.waitForTimeout(3_000);
    }
    await host.locator('.call-screen .remote-video-container').first()
        .waitFor({ state: 'visible', timeout: 30_000 });
}
