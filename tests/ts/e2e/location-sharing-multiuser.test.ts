/**
 * E2E test: multi-user live location sharing.
 *
 * Two users (Alice, Bob) sit in the same chat, each in their own browser context
 * with its own mocked geolocation. Proves the cross-user fan-out the single-user
 * test can't:
 *   - Alice shares -> Bob's banner shows "1 person is sharing location" (no Stop).
 *   - Bob opens the map and sees exactly Alice's marker; it tracks her movement.
 *   - Both share -> Alice's banner says "You and 1 other...", her map shows 2 markers.
 *   - Alice stops -> Bob still sees a live share (his own); Bob stops -> banners clear.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch).
 *
 * Run:
 *   npx vitest run tests/ts/e2e/location-sharing-multiuser.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
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

const tileLoaded = (page: Page) => page.waitForResponse(
    r => /maps[.-][^/]*\.(?:voxt\.ai|actual\.chat)\/(?:planet\/.*\.pbf|natural_earth\/.*\.png|fonts\/)/.test(r.url()) && r.ok(),
    { timeout: 20_000 });

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

async function startShare(page: Page, label: string) {
    await page.locator('.chat-message-editor .attach-btn').first().click({ force: true });
    await page.locator('text=Share location').first().click({ force: true });
    const modal = page.locator('.share-location-modal').first();
    await modal.waitFor({ state: 'visible', timeout: 10_000 });
    await modal.locator(`button:has-text("${label}")`).first().click();
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

        const aliceBanner = alice.locator('.live-location-banner').first();
        const bobBanner = bob.locator('.live-location-banner').first();

        // act — Alice starts a live share
        await startShare(alice, '15 min');

        // assert — Alice sees her own share (with a Stop button)
        await aliceBanner.waitFor({ state: 'visible', timeout: 20_000 });
        expect((await aliceBanner.innerText()).toLowerCase()).toContain('you are sharing your location');
        expect(await aliceBanner.locator('button:has-text("Stop")').count()).toBe(1);

        // assert — Bob (a viewer) sees Alice's share described as someone else's, with no Stop button
        await bobBanner.waitFor({ state: 'visible', timeout: 20_000 });
        expect((await bobBanner.innerText()).toLowerCase()).toContain('1 person is sharing location');
        expect(await bobBanner.locator('button:has-text("Stop")').count()).toBe(0);
        await bob.screenshot({ path: shot('bob-sees-alice') });

        // act — Bob opens the map from the banner
        const bobTiles = tileLoaded(bob);
        await bobBanner.locator('.c-body').first().click();
        const bobMap = bob.locator('.location-map-modal').first();
        await bobMap.waitFor({ state: 'visible', timeout: 10_000 });

        // assert — exactly Alice's single marker renders, on real tiles
        await bobMap.locator('.maplibregl-marker').first().waitFor({ state: 'visible', timeout: 15_000 });
        expect((await bobTiles).ok()).toBe(true);
        await bob.waitForTimeout(1_000);
        expect(await bobMap.locator('.maplibregl-marker').count()).toBe(1);
        await bob.screenshot({ path: shot('bob-map-1-marker') });

        // act — Alice moves; Bob's view should still show exactly one marker (it tracks, not duplicates)
        await aliceCtx.setGeolocation(ALICE_MOVED);
        await bob.waitForTimeout(2_500);
        expect(await bobMap.locator('.maplibregl-marker').count()).toBe(1);

        // act — Bob closes the map and starts his own share too
        await bob.keyboard.press('Escape');
        await bobMap.waitFor({ state: 'hidden', timeout: 10_000 }).catch(() => { /* ignore */ });
        await startShare(bob, '15 min');

        // assert — Alice's banner now reflects two sharers
        await alice.waitForTimeout(500);
        await expect.poll(
            async () => (await aliceBanner.innerText()).toLowerCase(),
            { timeout: 20_000 },
        ).toContain('you and 1 other are sharing location');

        // assert — Alice's map shows both markers (hers + Bob's)
        const aliceTiles = tileLoaded(alice);
        await aliceBanner.locator('.c-body').first().click();
        const aliceMap = alice.locator('.location-map-modal').first();
        await aliceMap.waitFor({ state: 'visible', timeout: 10_000 });
        await aliceMap.locator('.maplibregl-marker').first().waitFor({ state: 'visible', timeout: 15_000 });
        expect((await aliceTiles).ok()).toBe(true);
        await alice.waitForTimeout(1_500);
        expect(await aliceMap.locator('.maplibregl-marker').count()).toBe(2);
        await alice.screenshot({ path: shot('alice-map-2-markers') });
        await alice.keyboard.press('Escape');
        await aliceMap.waitFor({ state: 'hidden', timeout: 10_000 }).catch(() => { /* ignore */ });

        // act — Alice stops her share
        await aliceBanner.locator('button:has-text("Stop")').first().click();

        // assert — Alice's banner clears (she no longer shares and is the only one she tracked... )
        // Bob is still sharing, so from Bob's side the banner stays but is now "you are sharing".
        await expect.poll(
            async () => (await bobBanner.innerText()).toLowerCase(),
            { timeout: 20_000 },
        ).toContain('you are sharing your location');

        // assert — Alice, now a pure viewer of Bob's share, sees "1 person is sharing location"
        await expect.poll(
            async () => (await aliceBanner.innerText()).toLowerCase(),
            { timeout: 20_000 },
        ).toContain('1 person is sharing location');

        // act — Bob stops too
        await bobBanner.locator('button:has-text("Stop")').first().click();

        // assert — both banners disappear once no one is sharing
        await aliceBanner.waitFor({ state: 'hidden', timeout: 20_000 });
        await bobBanner.waitFor({ state: 'hidden', timeout: 20_000 });
        await alice.screenshot({ path: shot('all-stopped') });
    }, 240_000);
});
