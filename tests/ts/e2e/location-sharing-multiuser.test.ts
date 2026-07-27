/**
 * E2E test: multi-user live location sharing.
 *
 * Two users (Alice, Bob) sit in the same chat, each in their own browser context
 * with its own mocked geolocation. Proves the cross-user fan-out the single-user
 * test can't:
 *   - Alice shares -> Bob's inline map panel appears (#4067) with a "Share your
 *     location" CTA and no Stop button.
 *   - Bob expands the map and sees Alice's marker; it tracks her movement.
 *   - Both share -> both panels show Stop; Alice's map shows 2 markers.
 *   - Alice stops -> her panel flips to the viewer CTA (Bob still shares);
 *     Bob stops -> both panels disappear.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch).
 *
 * Run:
 *   npx vitest run tests/ts/e2e/location-sharing-multiuser.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import type { BrowserContext, Page } from 'playwright';
import {
    BASE_URL, TEST_EMAIL, TEST_EMAIL_2, connectBrowser, newUserContext, screenshot,
    skipOnboarding, waitForChatReady, waitForEditor, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e-multi', name);

const CHAT_URL = `${BASE_URL}/chat/the-actual-one`;

// Distinct positions so each user's marker is unmistakably their own.
const ALICE_START = { latitude: 51.5074, longitude: -0.1278, accuracy: 12 }; // London
const ALICE_MOVED = { latitude: 48.8566, longitude: 2.3522, accuracy: 12 };  // Paris
const BOB_START = { latitude: 52.5200, longitude: 13.4050, accuracy: 12 };   // Berlin

// An ACTUAL tile/glyph fetch from our maps.* proxy (not just the style JSON).
const TILE_URL_RE = /maps[.-][^/]*\.(?:voxt\.ai|actual\.chat)\/(?:planet\/.*\.pbf|natural_earth\/.*\.png|fonts\/)/;

const tileLoaded = (page: Page) => page.waitForResponse(
    r => TILE_URL_RE.test(r.url()) && r.ok(),
    { timeout: 30_000 });

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

// A failed test must not leak its live share into the next run: re-sharing over an
// active share mints a NEW SharedLocation and orphans the old row server-side, where
// nothing can stop it until it expires on its own.
async function stopSharingIfAny(page: Page | undefined) {
    if (!page)
        return;

    for (let i = 0; i < 2; i++) {
        await page.keyboard.press('Escape').catch(() => { /* ignore */ });
        await page.waitForTimeout(300);
    }
    const stopButton = page.locator('.visual-activity-panel .map-panel .btn-stop-sharing').first();
    if (await stopButton.isVisible({ timeout: 1_000 }).catch(() => false)) {
        await stopButton.click().catch(() => { /* ignore */ });
        await page.locator('.visual-activity-panel .map-panel').first()
            .waitFor({ state: 'hidden', timeout: 15_000 }).catch(() => { /* ignore */ });
    }
}

async function startShare(page: Page, label: string) {
    await page.locator('.chat-message-editor .attach-btn').first().click({ force: true });
    await page.locator('.ac-menu-item:has-text("Location")').first().click({ force: true });
    const modal = page.locator('.share-location-modal').first();
    await modal.waitFor({ state: 'visible', timeout: 10_000 });
    await modal.locator('.c-share-live').first().click();
    await modal.locator(`.c-duration-menu .c-menu-item:has-text("${label}")`).first().click();
}

describe('multi-user location sharing', () => {
    let conn: BrowserConnection;
    let aliceCtx: BrowserContext;
    let bobCtx: BrowserContext;
    let alice: Page;
    let bob: Page;

    beforeAll(async () => {
        conn = await connectBrowser();
        // Sequential sign-ins: parallel ones race on the shared server flow (see vitest.config.e2e.ts).
        ({ context: aliceCtx, page: alice } = await newUserContext(conn, TEST_EMAIL, ALICE_START));
        ({ context: bobCtx, page: bob } = await newUserContext(conn, TEST_EMAIL_2, BOB_START));
    }, 180_000);

    afterEach(async () => {
        await stopSharingIfAny(alice);
        await stopSharingIfAny(bob);
    }, 60_000);

    afterAll(async () => {
        await aliceCtx.close().catch(() => { /* ignore */ });
        await bobCtx.close().catch(() => { /* ignore */ });
        if (conn.ownsBrowser) {
            await conn.context.close().catch(() => { /* ignore */ });
            await conn.browser.close().catch(() => { /* ignore */ });
        }
    });

    it('each user sees the other\'s live share, its movement, and independent stop', async () => {
        // arrange — both users open the same chat
        await openChat(alice);
        await openChat(bob);

        const alicePanel = alice.locator('.visual-activity-panel .map-panel').first();
        const bobPanel = bob.locator('.visual-activity-panel .map-panel').first();

        // act — Alice starts a live share (arm both tile listeners first: each user's
        // inline panel starts fetching tiles as soon as the share reaches them)
        const aliceTiles = tileLoaded(alice);
        const bobTiles = tileLoaded(bob);
        await startShare(alice, '15 min');

        // assert — Alice sees her own share (with a Stop button, no viewer CTA)
        await alicePanel.waitFor({ state: 'visible', timeout: 20_000 });
        await alicePanel.locator('.btn-stop-sharing').first().waitFor({ state: 'visible', timeout: 10_000 });
        expect(await alicePanel.locator('.btn-share-location').count()).toBe(0);
        expect((await aliceTiles).ok()).toBe(true);

        // assert — no Call/Map switch on either side: the map is the only panel activity (#4067)
        expect(await alice.locator('.call-map-switch').count()).toBe(0);
        expect(await bob.locator('.call-map-switch').count()).toBe(0);

        // assert — the old live-location banner stays hidden (the panel replaced it)
        expect(await alice.locator('.live-location-banner').isVisible().catch(() => false)).toBe(false);

        // assert — Bob (a viewer) gets the panel with no stop button and a "Share your
        // location" call-to-action instead (#4057)
        await bobPanel.waitFor({ state: 'visible', timeout: 20_000 });
        expect(await bobPanel.locator('.btn-stop-sharing').count()).toBe(0);
        await bobPanel.locator('.btn-share-location').first().waitFor({ state: 'visible', timeout: 10_000 });
        expect((await bobTiles).ok()).toBe(true);
        await bob.screenshot({ path: shot('bob-sees-alice') });

        // assert — Bob's inline panel shows both markers: Alice's share + his own location
        await expect.poll(
            async () => bobPanel.locator('.maplibregl-marker').count(),
            { timeout: 20_000 },
        ).toBe(2);

        // assert — the live card Alice posted offers Bob a share-back button, while Alice's own
        // copy of the same card doesn't (#4057)
        const bobCard = bob.locator('.location-message').last();
        await expect.poll(
            async () => bobCard.locator('.c-share-back').count(),
            { timeout: 20_000 },
        ).toBe(1);
        expect(await alice.locator('.location-message').last().locator('.c-share-back').count()).toBe(0);

        // act — Bob expands the map from the inline panel
        await bobPanel.locator('.c-expand').first().click();
        const bobMap = bob.locator('.location-map-modal').first();
        await bobMap.waitFor({ state: 'visible', timeout: 10_000 });

        // assert — Alice's marker plus Bob's own-location marker render in the modal
        await bobMap.locator('.maplibregl-marker').first().waitFor({ state: 'visible', timeout: 15_000 });
        await bob.waitForTimeout(1_000);
        expect(await bobMap.locator('.maplibregl-marker').count()).toBe(2);

        // assert — the participants list shows exactly Alice: not marked "(you)", with an
        // "Updated ..." status and her share's countdown; Bob (not sharing) gets no row,
        // only his own map marker
        const bobRow = bobMap.locator('.c-participant').first();
        await bobRow.waitFor({ state: 'visible', timeout: 10_000 });
        expect(await bobMap.locator('.c-participant').count()).toBe(1);
        const bobRowText = (await bobRow.innerText()).toLowerCase();
        expect(bobRowText).toContain('updated');
        expect(bobRowText).not.toContain('(you)');
        await bob.screenshot({ path: shot('bob-map-1-marker') });

        // act — Alice moves; Bob's view should still show the same two markers (it tracks, not duplicates)
        await aliceCtx.setGeolocation(ALICE_MOVED);
        await bob.waitForTimeout(2_500);
        expect(await bobMap.locator('.maplibregl-marker').count()).toBe(2);

        // act — Bob closes the map and starts his own share too
        await bob.keyboard.press('Escape');
        await bobMap.waitFor({ state: 'hidden', timeout: 10_000 }).catch(() => { /* ignore */ });
        await startShare(bob, '15 min');

        // assert — Bob's panel flips to the sharer state: Stop button, no viewer CTA
        await bobPanel.locator('.btn-stop-sharing').first().waitFor({ state: 'visible', timeout: 20_000 });
        expect(await bobPanel.locator('.btn-share-location').count()).toBe(0);

        // assert — Alice's inline panel now shows both markers (hers + Bob's)
        await expect.poll(
            async () => alicePanel.locator('.maplibregl-marker').count(),
            { timeout: 20_000 },
        ).toBe(2);

        // act — Alice expands her map
        await alicePanel.locator('.c-expand').first().click();
        const aliceMap = alice.locator('.location-map-modal').first();
        await aliceMap.waitFor({ state: 'visible', timeout: 10_000 });
        await aliceMap.locator('.maplibregl-marker').first().waitFor({ state: 'visible', timeout: 15_000 });
        await alice.waitForTimeout(1_500);
        expect(await aliceMap.locator('.maplibregl-marker').count()).toBe(2);

        // assert — two participant rows: Alice's own row first ("(you)" + countdown), then
        // Bob's with his countdown and his distance from her (Paris -> Berlin, hundreds of km)
        expect(await aliceMap.locator('.c-participant').count()).toBe(2);
        const rows = await aliceMap.locator('.c-participant').allInnerTexts();
        expect(rows[0].toLowerCase()).toContain('(you)');
        expect(rows[1].toLowerCase()).not.toContain('(you)');
        expect(rows[1].toLowerCase()).toMatch(/\d+ km from you/);
        await alice.screenshot({ path: shot('alice-map-2-markers') });
        await alice.keyboard.press('Escape');
        await aliceMap.waitFor({ state: 'hidden', timeout: 10_000 }).catch(() => { /* ignore */ });

        // act + assert — clicking a LIVE location message opens the shared map with
        // everyone currently sharing (Telegram-style), not just that message's share
        await alice.locator('.location-message').last().click();
        await aliceMap.waitFor({ state: 'visible', timeout: 10_000 });
        await expect.poll(
            async () => aliceMap.locator('.c-participant').count(),
            { timeout: 15_000 },
        ).toBe(2);
        await alice.keyboard.press('Escape');
        await aliceMap.waitFor({ state: 'hidden', timeout: 10_000 }).catch(() => { /* ignore */ });

        // act — Alice stops her share
        await alicePanel.locator('.btn-stop-sharing').first().click();

        // assert — Bob is still sharing, so his panel keeps the Stop button
        await expect.poll(
            async () => bobPanel.locator('.btn-stop-sharing').count(),
            { timeout: 20_000 },
        ).toBe(1);

        // assert — Alice, now a pure viewer of Bob's share, keeps the panel but gets the
        // viewer CTA instead of Stop
        await alicePanel.locator('.btn-share-location').first().waitFor({ state: 'visible', timeout: 20_000 });
        expect(await alicePanel.locator('.btn-stop-sharing').count()).toBe(0);

        // act — Bob stops too
        await bobPanel.locator('.btn-stop-sharing').first().click();

        // assert — both panels disappear once no one is sharing
        await alicePanel.waitFor({ state: 'hidden', timeout: 20_000 });
        await bobPanel.waitFor({ state: 'hidden', timeout: 20_000 });

        // assert — the share-back button goes away with the share it answered (#4057)
        await expect.poll(
            async () => bobCard.locator('.c-share-back').count(),
            { timeout: 20_000 },
        ).toBe(0);
        await alice.screenshot({ path: shot('all-stopped') });
    }, 240_000);
});
