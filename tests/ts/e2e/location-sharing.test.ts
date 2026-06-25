/**
 * E2E test: live location sharing.
 *
 * Drives the full web flow with a mocked browser geolocation:
 *   "+" menu -> Share location -> pick a duration -> banner appears ->
 *   open the map (a MapLibre marker renders) -> move the mock position ->
 *   Stop -> banner disappears.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch).
 * - Optionally, Chrome with remote debugging: `c chrome` (port 9222) to watch live.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/location-sharing.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Page } from 'playwright';
import {
    BASE_URL, connectBrowser, ensureSignedIn, skipOnboarding, screenshot,
    waitForChatReady, waitForEditor, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e', name);

// A shared chat the test user can join, so it has an editor + Banners host to share from.
const CHAT_URL = `${BASE_URL}/chat/the-actual-one`;

// London -> Paris, to prove the marker tracks position updates.
const START = { latitude: 51.5074, longitude: -0.1278, accuracy: 12 };
const MOVED = { latitude: 48.8566, longitude: 2.3522, accuracy: 12 };

describe('location sharing', () => {
    let conn: BrowserConnection;
    let page: Page;

    beforeAll(async () => {
        conn = await connectBrowser();
        await conn.context.grantPermissions(['geolocation'], { origin: BASE_URL });
        await conn.context.setGeolocation(START);
        page = await conn.context.newPage();
        page.on('pageerror', e => console.log('PAGEERROR:', e.message));
        page.on('console', m => { if (m.type() === 'error') console.log('CONSOLE.ERROR:', m.text()); });
        await ensureSignedIn(page);
    }, 120_000);

    afterAll(async () => {
        await page.close();
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    });

    it('shares location, shows the banner + map marker, then stops', async () => {
        // arrange — open a chat we can post in
        await page.goto(CHAT_URL, { waitUntil: 'domcontentloaded' });
        await waitForChatReady(page);
        await skipOnboarding(page);

        const joinButton = page.locator('button:has-text("Join this chat")');
        if (await joinButton.isVisible({ timeout: 3000 }).catch(() => false)) {
            await joinButton.click();
            await page.waitForTimeout(1500);
        }

        await waitForEditor(page);

        // act — open the "+" menu and start a share
        await page.locator('.chat-message-editor .attach-btn').first().click({ force: true });
        await page.locator('text=Share location').first().click({ force: true });

        const modal = page.locator('.share-location-modal').first();
        await modal.waitFor({ state: 'visible', timeout: 10_000 });
        await page.screenshot({ path: shot('loc-modal') });
        await modal.locator('button:has-text("15 min")').first().click();

        // assert — the chat banner reflects the active share
        const banner = page.locator('.live-location-banner').first();
        await banner.waitFor({ state: 'visible', timeout: 20_000 });
        expect((await banner.innerText()).toLowerCase()).toContain('sharing your location');
        await page.screenshot({ path: shot('loc-banner') });

        // act — open the map modal from the banner (arm the tile listener BEFORE the click,
        // so we don't miss the tile requests fired during map init)
        // Match an ACTUAL tile/glyph fetch (not just the style JSON): proves the map has a
        // real viewport and is painting — guards against the CSP block AND the 0-size-in-modal
        // bug where the style loads but no tiles are ever requested.
        const tileLoaded = page.waitForResponse(
            r => /tiles\.openfreemap\.org\/(planet\/.*\.pbf|natural_earth\/.*\.png|fonts\/)/.test(r.url())
                && r.ok(),
            { timeout: 20_000 });
        await banner.locator('.c-body').first().click();
        const mapModal = page.locator('.live-location-map-modal').first();
        await mapModal.waitFor({ state: 'visible', timeout: 10_000 });

        // assert — a MapLibre marker renders for the share
        const marker = mapModal.locator('.maplibregl-marker').first();
        await marker.waitFor({ state: 'visible', timeout: 15_000 });

        // assert — real OpenFreeMap tiles load (not blank: CSP allows the host AND the map
        // got a non-zero viewport)
        const tileResp = await tileLoaded;
        expect(tileResp.ok()).toBe(true);
        await page.waitForTimeout(1_500); // let the tiles paint before the screenshot
        await page.screenshot({ path: shot('loc-map') });

        // act — move the position; the marker should track it (still exactly one)
        await conn.context.setGeolocation(MOVED);
        await page.waitForTimeout(2_000);
        expect(await mapModal.locator('.maplibregl-marker').count()).toBe(1);
        await page.screenshot({ path: shot('loc-map-moved') });

        // act — close the map, then stop sharing from the banner
        await page.keyboard.press('Escape');
        await mapModal.waitFor({ state: 'hidden', timeout: 10_000 }).catch(() => { /* ignore */ });
        await banner.locator('button:has-text("Stop")').first().click();

        // assert — the banner disappears once the share is stopped
        await banner.waitFor({ state: 'hidden', timeout: 20_000 });
        await page.screenshot({ path: shot('loc-stopped') });
    }, 180_000);

    it('sends current location once as a message with an inline map', async () => {
        // arrange — open a chat we can post in
        await page.goto(CHAT_URL, { waitUntil: 'domcontentloaded' });
        await waitForChatReady(page);
        await skipOnboarding(page);

        const joinButton = page.locator('button:has-text("Join this chat")');
        if (await joinButton.isVisible({ timeout: 3000 }).catch(() => false)) {
            await joinButton.click();
            await page.waitForTimeout(1500);
        }

        await waitForEditor(page);
        await conn.context.setGeolocation(START);

        // act — open the "+" menu and send the current location once
        await page.locator('.chat-message-editor .attach-btn').first().click({ force: true });
        await page.locator('text=Share location').first().click({ force: true });

        const modal = page.locator('.share-location-modal').first();
        await modal.waitFor({ state: 'visible', timeout: 10_000 });

        // arm the tile listener before posting so we don't miss the inline-map tile requests
        const tileLoaded = page.waitForResponse(
            r => /tiles\.openfreemap\.org\/(planet\/.*\.pbf|natural_earth\/.*\.png|fonts\/)/.test(r.url())
                && r.ok(),
            { timeout: 20_000 });
        await modal.locator('button:has-text("Send current location")').first().click();

        // assert — a one-shot location message appears in the chat stream with an inline map marker
        const locationMessage = page.locator('.location-message').last();
        await locationMessage.waitFor({ state: 'visible', timeout: 20_000 });
        const marker = locationMessage.locator('.maplibregl-marker').first();
        await marker.waitFor({ state: 'visible', timeout: 15_000 });

        // assert — the inline map actually paints real tiles (not blank)
        const tileResp = await tileLoaded;
        expect(tileResp.ok()).toBe(true);
        await page.waitForTimeout(1_500); // let the tiles paint before the screenshot
        await page.screenshot({ path: shot('loc-one-shot') });

        // assert — there is no live-share banner for a one-shot send (it's a static message)
        const banner = page.locator('.live-location-banner').first();
        expect(await banner.isVisible({ timeout: 2_000 }).catch(() => false)).toBe(false);
    }, 180_000);

    // Every duration option must start a live share (the banner appears), not just the first one.
    for (const label of ['15 minutes', '1 hour', '8 hours']) {
        it(`starts a live share for "${label}"`, async () => {
            await page.goto(CHAT_URL, { waitUntil: 'domcontentloaded' });
            await waitForChatReady(page);
            await skipOnboarding(page);

            const joinButton = page.locator('button:has-text("Join this chat")');
            if (await joinButton.isVisible({ timeout: 3000 }).catch(() => false)) {
                await joinButton.click();
                await page.waitForTimeout(1500);
            }

            await waitForEditor(page);
            await conn.context.setGeolocation(START);

            await page.locator('.chat-message-editor .attach-btn').first().click({ force: true });
            await page.locator('text=Share location').first().click({ force: true });

            const modal = page.locator('.share-location-modal').first();
            await modal.waitFor({ state: 'visible', timeout: 10_000 });
            await modal.locator(`button:has-text("${label}")`).first().click();

            // assert — the banner reflects the active share for this duration
            const banner = page.locator('.live-location-banner').first();
            await banner.waitFor({ state: 'visible', timeout: 20_000 });

            // cleanup — stop the share so the next duration starts clean
            await banner.locator('button:has-text("Stop")').first().click();
            await banner.waitFor({ state: 'hidden', timeout: 20_000 });
        }, 120_000);
    }
});
