/**
 * E2E test: raise hand and emoji reactions in a video call (#4603).
 *
 * Two users (Alice, Bob) turn their cameras on in the same chat, which starts a
 * live session - a hand exists only while there is one. Then:
 *   - Bob raises his hand -> Alice sees the hand badge on Bob's tile.
 *   - Bob sends an emoji -> it floats over Alice's video panel with his name.
 *   - Alice, the host (she streamed first), lowers Bob's hand from the Call tab
 *     -> the badge goes away and Bob is told his hand was lowered.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch), locally: the feature is incomplete UI, which the
 *   test turns on for both accounts - test agents are admins only on a local server.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/raise-hand-video.test.ts --config vitest.config.e2e.ts
 */

import * as path from 'path';
import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import type { BrowserContext, Page } from 'playwright';
import {
    BASE_URL, TEST_EMAIL, TEST_EMAIL_2, connectBrowser, newUserContext, screenshot, setIncompleteUI,
    skipOnboarding, waitForChatReady, waitForEditor, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e-raise-hand', name);

const CHAT_URL = `${BASE_URL}/chat/the-actual-one`;
const SPEECH_WAV = path.resolve('lib/data/test-audio-1.wav');

async function openChat(page: Page) {
    await page.goto(CHAT_URL, { waitUntil: 'domcontentloaded' });
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
async function startRecording(page: Page) {
    // Recording is restored with the account's active chats, so a blind click can turn it off.
    const recordOn = page.locator('.chat-audio-panel .recorder-wrapper.record-on').first();
    if (!await recordOn.waitFor({ state: 'attached', timeout: 3_000 }).then(() => true, () => false))
        await page.locator('.chat-audio-panel .recorder-wrapper button').first().click();
    // record-on is the intent; applying-changes lasts until the recorder has really started
    await page.locator('.chat-audio-panel .recorder-wrapper.record-on:not(.applying-changes)').first()
        .waitFor({ state: 'attached', timeout: 30_000 });
}

async function startCamera(page: Page) {
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

async function expandVideoPanel(page: Page) {
    // The expand button fades in (show-with-delay), and a click that lands before then is lost.
    const panel = page.locator('.video-panel').first();
    await expect.poll(async () => {
        if (!(await panel.getAttribute('class'))?.includes('expanded'))
            await panel.locator('.expand-btn').first().click({ timeout: 2_000 }).catch(() => { /* retried */ });
        return (await panel.getAttribute('class')) ?? '';
    }, { timeout: 20_000, interval: 1_000 }).toContain('expanded');
}

async function collapseVideoPanel(page: Page) {
    const panel = page.locator('.video-panel').first();
    if ((await panel.getAttribute('class'))?.includes('expanded'))
        await panel.locator('.expand-btn').first().click();
    await expect.poll(async () => (await panel.getAttribute('class')) ?? '', { timeout: 10_000 })
        .not.toContain('expanded');
}

async function setGallery(page: Page, isOn: boolean) {
    const panel = page.locator('.video-panel').first();
    const isGallery = async () => ((await panel.getAttribute('class')) ?? '').includes('layout-equal');
    if (await isGallery() !== isOn)
        await panel.locator('.layout-toggle-btn').first().click();
    await expect.poll(isGallery, { timeout: 10_000 }).toBe(isOn);
}

async function openCallTab(page: Page) {
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
async function hangUpIfAny(page: Page | undefined) {
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
    if (await page.locator('.chat-audio-panel .recorder-wrapper.record-on').first().isVisible({ timeout: 1_000 }).catch(() => false))
        await page.locator('.chat-audio-panel .recorder-wrapper button').first().click().catch(() => { /* ignore */ });
}

describe('raise hand and reactions in a video call', () => {
    let conn: BrowserConnection;
    let aliceCtx: BrowserContext;
    let bobCtx: BrowserContext;
    let alice: Page;
    let bob: Page;

    beforeAll(async () => {
        // Real speech, not the fake mic's beep: silent recording idles out after 30s, and with no
        // recorder left the session closes. Speech also registers the audio streams with the session.
        conn = await connectBrowser({ fakeAudioFile: SPEECH_WAV });
        // Sequential sign-ins: parallel ones race on the shared server flow (see vitest.config.e2e.ts).
        ({ context: aliceCtx, page: alice } = await newUserContext(conn, TEST_EMAIL));
        ({ context: bobCtx, page: bob } = await newUserContext(conn, TEST_EMAIL_2));
        // The fake-device flags answer getUserMedia, but the app reads the Permissions API first
        // and shows "No microphone access" while a fresh context still reports "prompt".
        for (const ctx of [aliceCtx, bobCtx])
            await ctx.grantPermissions(['microphone', 'camera'], { origin: BASE_URL });
        // Hands and reactions are incomplete UI for now
        for (const page of [alice, bob])
            await setIncompleteUI(page, true);
    }, 180_000);

    afterEach(async () => {
        await hangUpIfAny(bob);
        await hangUpIfAny(alice);
    }, 60_000);

    afterAll(async () => {
        // Unload before closing: a context closed outright leaves its server-side circuit alive for
        // about a minute, still heartbeating as this account - and when it finally goes, its leave
        // removes the next run's participation record (one record per author), closing that session.
        for (const page of [alice, bob]) {
            await setIncompleteUI(page, false).catch(() => { /* ignore */ });
            await page.goto('about:blank').catch(() => { /* ignore */ });
        }
        await aliceCtx.close().catch(() => { /* ignore */ });
        await bobCtx.close().catch(() => { /* ignore */ });
        if (conn.ownsBrowser) {
            await conn.context.close().catch(() => { /* ignore */ });
            await conn.browser.close().catch(() => { /* ignore */ });
        }
    });

    it('a raised hand shows on the tile, reactions float, and the host can lower the hand', async () => {
        // arrange - Alice starts first, so she is the host
        await openChat(alice);
        await openChat(bob);
        await startSession();
        const aliceBobTile = alice.locator('.video-panel .remote-video-container').first();
        const bobName = (await aliceBobTile.locator('.video-participant-label span').first().innerText()).trim();
        expect(bobName.length).toBeGreaterThan(0);

        // act - Bob expands the panel (the React button lives in its footer) and raises his hand
        await expandVideoPanel(bob);
        const reactButton = bob.locator('.video-panel-footer .btn-react').first();
        await reactButton.waitFor({ state: 'visible', timeout: 20_000 });
        await reactButton.click();
        const bobMenu = bob.locator('.call-reactions-menu').first();
        await bobMenu.waitFor({ state: 'visible', timeout: 10_000 });
        await bobMenu.locator('.ac-menu-item:has(.icon-hand)').first().click();

        // assert - inline panel: the hand sits right of Bob's label on Alice's side
        await aliceBobTile.locator('.video-tile-caption .video-hand-badge')
            .waitFor({ state: 'visible', timeout: 15_000 });
        await expect.poll(async () => reactButton.getAttribute('class'), { timeout: 10_000 }).toContain('on');
        await alice.screenshot({ path: shot('1-inline-next-to-label') });

        // assert - Bob's own camera is a small tile in his speaker view: the hand is in its top-left corner
        await bob.locator('.video-streaming-preview .video-hand-badge.corner')
            .waitFor({ state: 'visible', timeout: 10_000 });
        await bob.screenshot({ path: shot('2-small-tile-corner') });

        // assert - speaker view: Bob is the speaker, so his hand is a button-sized indicator in the header
        await expandVideoPanel(alice);
        await alice.locator('.video-panel-header .btn-hand').waitFor({ state: 'visible', timeout: 10_000 });
        await expect.poll(async () => aliceBobTile.locator('.video-hand-badge:visible').count()).toBe(0);
        await alice.screenshot({ path: shot('3-speaker-view-header') });

        // assert - gallery view: back right of the label
        await setGallery(alice, true);
        try {
            await aliceBobTile.locator('.video-tile-caption .video-hand-badge')
                .waitFor({ state: 'visible', timeout: 10_000 });
            await alice.screenshot({ path: shot('4-gallery-next-to-label') });
        }
        finally {
            await setGallery(alice, false);
        }

        // act - Bob sends a thumbs-up
        await reactButton.click();
        await bobMenu.waitFor({ state: 'visible', timeout: 10_000 });
        await bobMenu.locator('.reaction-select-reaction').first().click();

        // assert - it floats over Alice's panel, labelled with Bob's name
        const aliceReaction = alice.locator('.video-panel .call-reactions-overlay .c-reaction').first();
        await aliceReaction.waitFor({ state: 'attached', timeout: 15_000 });
        expect((await aliceReaction.locator('.c-name').innerText()).trim()).toBe(bobName);
        await alice.screenshot({ path: shot('5-reaction') });

        // act - Alice, the host, lowers Bob's hand from the Call tab (outside the expanded panel)
        await collapseVideoPanel(alice);
        await openCallTab(alice);
        const lowerButton = alice.locator('.live-session-member-list .c-hand-btn').first();
        await lowerButton.waitFor({ state: 'visible', timeout: 15_000 });
        await alice.screenshot({ path: shot('6-call-tab') });
        await lowerButton.click();

        // assert - the badge goes away and Bob is told his hand was lowered
        await expect.poll(async () => aliceBobTile.locator('.video-hand-badge').count(), { timeout: 15_000 })
            .toBe(0);
        await bob.getByText('Your hand was lowered').first().waitFor({ state: 'visible', timeout: 15_000 });
        await expect.poll(async () => reactButton.getAttribute('class'), { timeout: 10_000 })
            .not.toContain(' on');
        await bob.screenshot({ path: shot('7-hand-lowered') });
    }, 300_000);

    async function startSession() {
        // A session has to latch before a hand can go up, and it closes again if nobody is recording
        // yet when the cameras start. The speech WAV makes that rare; the retry covers the rest.
        for (let attempt = 1; ; attempt++) {
            await startRecording(alice);
            await startRecording(bob);
            await startCamera(alice);
            await startCamera(bob);
            const canReact = await bob.locator('.video-panel-footer .btn-react').first()
                .waitFor({ state: 'attached', timeout: 20_000 }).then(() => true, () => false);
            if (canReact)
                break;
            if (attempt === 3)
                throw new Error('The live session never latched: no React button after 3 attempts');

            console.log(`Live session did not latch (attempt ${attempt}), restarting both cameras`);
            await hangUpIfAny(bob);
            await hangUpIfAny(alice);
            await alice.waitForTimeout(3_000);
        }
        await alice.locator('.video-panel .remote-video-container').first()
            .waitFor({ state: 'visible', timeout: 30_000 });
    }
});
