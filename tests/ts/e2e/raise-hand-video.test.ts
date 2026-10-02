/**
 * E2E test: raise hand and emoji reactions in a video call (#4603).
 *
 * Two users (Alice, Bob) turn their cameras on in the same chat, which starts a
 * live session - a hand exists only while there is one. Then:
 *   - Bob raises his hand -> Alice sees the hand badge on Bob's tile.
 *   - Bob sends an emoji -> it floats over Alice's video panel with his name.
 *   - Alice, the host (she streamed first), lowers Bob's hand from the Call tab
 *     -> the badge goes away and Bob is told his hand was lowered.
 *   - Bob raises his hand while muted, then unmutes and talks -> the hand lowers by itself (#5037).
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch), locally: the feature is incomplete UI, which the
 *   test turns on for both accounts - test agents are admins only on a local server.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/raise-hand-video.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import type { BrowserContext, Page } from 'playwright';
import {
    BASE_URL, TEST_EMAIL, TEST_EMAIL_2, connectBrowser, newUserContext, screenshot, setIncompleteUI,
    type BrowserConnection,
} from './helpers';
import {
    SPEECH_WAV, collapseVideoPanel, countVideoElements, expandVideoPanel, hangUpIfAny, markVideoElements,
    openCallChat, openCallTab, setGallery, startRecording, startSession, stopRecording,
} from './video-call';

const shot = (name: string) => screenshot('e2e-raise-hand', name);

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
        await openCallChat(alice);
        await openCallChat(bob);
        await startSession(alice, bob);
        const aliceBobTile = alice.locator('.video-panel .remote-video-container').first();
        const bobName = (await aliceBobTile.locator('.video-participant-label span').first().innerText()).trim();
        expect(bobName.length).toBeGreaterThan(0);
        // The fake mic never stops talking, and a hand lowers itself once its owner says a few words
        await stopRecording(bob);

        // act - Bob expands the panel (the React button lives in its footer) and raises his hand
        await expandVideoPanel(bob);
        const reactButton = bob.locator('.video-panel-footer .btn-react').first();
        await reactButton.waitFor({ state: 'visible', timeout: 20_000 });
        await reactButton.click();
        const bobMenu = bob.locator('.call-reactions-menu').first();
        await bobMenu.waitFor({ state: 'visible', timeout: 10_000 });
        const aliceVideoCount = await markVideoElements(alice);
        const bobVideoCount = await markVideoElements(bob);
        expect(aliceVideoCount).toBeGreaterThan(0);
        expect(bobVideoCount).toBeGreaterThan(0);
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

        // assert - the hand re-rendered the tiles without recreating them: same video elements, no new ones
        expect(await countVideoElements(alice)).toEqual({ marked: aliceVideoCount, unmarked: 0 });
        expect(await countVideoElements(bob)).toEqual({ marked: bobVideoCount, unmarked: 0 });

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
        const lowerButton = alice.locator('.live-session-member-list .btn-lower-hand').first();
        await lowerButton.waitFor({ state: 'visible', timeout: 15_000 });
        await alice.screenshot({ path: shot('6-call-tab') });
        const aliceVideoCountBeforeLower = await markVideoElements(alice);
        expect(aliceVideoCountBeforeLower).toBeGreaterThan(0);
        await lowerButton.click();

        // assert - the badge goes away and Bob is told his hand was lowered
        await expect.poll(async () => aliceBobTile.locator('.video-hand-badge').count(), { timeout: 15_000 })
            .toBe(0);
        await bob.getByText('Your hand was lowered').first().waitFor({ state: 'visible', timeout: 15_000 });
        await expect.poll(async () => reactButton.getAttribute('class'), { timeout: 10_000 })
            .not.toContain(' on');
        await bob.screenshot({ path: shot('7-hand-lowered') });
        expect(await countVideoElements(alice)).toEqual({ marked: aliceVideoCountBeforeLower, unmarked: 0 });
    }, 300_000);

    it('a raised hand lowers itself once its owner says several words', async () => {
        // arrange - Bob is muted, so his hand stays up for as long as he says nothing
        await openCallChat(alice);
        await openCallChat(bob);
        await startSession(alice, bob);
        await stopRecording(bob);
        const aliceBobTile = alice.locator('.video-panel .remote-video-container').first();
        const aliceBadge = aliceBobTile.locator('.video-tile-caption .video-hand-badge');
        const reactButton = bob.locator('.video-panel-footer .btn-react').first();
        await expandVideoPanel(bob);
        await reactButton.waitFor({ state: 'visible', timeout: 20_000 });
        await reactButton.click();
        const bobMenu = bob.locator('.call-reactions-menu').first();
        await bobMenu.waitFor({ state: 'visible', timeout: 10_000 });
        await bobMenu.locator('.ac-menu-item:has(.icon-hand)').first().click();
        await aliceBadge.waitFor({ state: 'visible', timeout: 15_000 });
        await expect.poll(async () => reactButton.getAttribute('class'), { timeout: 10_000 }).toContain('on');
        // The hand must outlast the echo of Bob's own raise before the wait below means anything
        await alice.waitForTimeout(5_000);
        expect(await aliceBadge.count()).toBe(1);
        await alice.screenshot({ path: shot('8-raised-while-muted') });
        await bob.screenshot({ path: shot('9-raised-while-muted-own') });

        // act - Bob unmutes, and the fake mic starts talking
        await collapseVideoPanel(bob);
        await startRecording(bob);

        // assert - nobody touched the hand, yet it's down on both sides
        await expect.poll(async () => aliceBadge.count(), { timeout: 60_000 }).toBe(0);
        await alice.screenshot({ path: shot('10-lowered-after-speaking') });
        await expect.poll(async () => bob.locator('.video-panel .video-hand-badge').count(), { timeout: 15_000 })
            .toBe(0);
        // It was Bob's own client that lowered it, so the "lowered by someone else" notice must not show
        expect(await bob.getByText('Your hand was lowered').count()).toBe(0);
        await bob.screenshot({ path: shot('11-lowered-after-speaking-own') });
        await expandVideoPanel(bob);
        await expect.poll(async () => reactButton.getAttribute('class'), { timeout: 10_000 })
            .not.toContain(' on');
    }, 300_000);
});
