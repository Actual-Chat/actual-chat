/**
 * E2E test: the share-location modal without the location permission (#4104).
 *
 * On a phone the modal opens before location access is granted, and its map area
 * stayed an empty gray box. It must show "Enable location permission to show map."
 * instead, and pick the map up once the permission is granted — without reopening
 * the modal, which only works if the permission state feeds the marker's compute
 * chain.
 *
 * Runs in its own browser context: a fresh one reports geolocation as "prompt",
 * while the shared context other tests use has already granted it.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch).
 *
 * Run:
 *   npx vitest run tests/ts/e2e/location-permission-prompt.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { BrowserContext, Page } from 'playwright';
import {
    BASE_URL, TEST_EMAIL, connectBrowser, newUserContext, screenshot, skipOnboarding,
    waitForChatReady, waitForEditor, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e', name);

const CHAT_URL = `${BASE_URL}/chat/the-actual-one`;
const START = { latitude: 51.5074, longitude: -0.1278, accuracy: 12 };

describe('share location modal without the location permission', () => {
    let conn: BrowserConnection;
    let context: BrowserContext;
    let page: Page;

    beforeAll(async () => {
        conn = await connectBrowser();
        ({ context, page } = await newUserContext(conn, TEST_EMAIL));
    }, 120_000);

    afterAll(async () => {
        await page.close().catch(() => { /* ignore */ });
        await context.close().catch(() => { /* ignore */ });
        if (conn.ownsBrowser) {
            await conn.context.close().catch(() => { /* ignore */ });
            await conn.browser.close().catch(() => { /* ignore */ });
        }
    });

    it('prompts to enable the permission, then shows the map once it is granted', async () => {
        // arrange — open a chat we can share from
        await page.goto(CHAT_URL, { waitUntil: 'domcontentloaded' });
        await waitForChatReady(page);
        await skipOnboarding(page);

        const joinButton = page.locator('button:has-text("Join this chat")');
        if (await joinButton.isVisible({ timeout: 3000 }).catch(() => false)) {
            await joinButton.click();
            await page.waitForTimeout(1500);
        }
        await waitForEditor(page);

        // arrange — a live share leaked by an earlier run would put an own marker on the
        // map area, and the prompt only replaces a map that has nothing to show
        const leakedStop = page.locator('.visual-activity-panel .map-panel .btn-stop-sharing').first();
        if (await leakedStop.isVisible({ timeout: 1_000 }).catch(() => false)) {
            await leakedStop.click().catch(() => { /* ignore */ });
            await page.locator('.visual-activity-panel .map-panel').first()
                .waitFor({ state: 'hidden', timeout: 15_000 }).catch(() => { /* ignore */ });
        }

        // act — open the share-location modal with geolocation still ungranted
        await page.locator('.chat-message-editor .attach-btn').first().click({ force: true });
        await page.locator('.ac-menu-item:has-text("Location")').first().click({ force: true });
        const modal = page.locator('.share-location-modal').first();
        await modal.waitFor({ state: 'visible', timeout: 10_000 });

        // assert — the map area carries the prompt instead of a map, and the locate /
        // map-type buttons are gone with it (they act on a map that isn't there)
        const prompt = modal.locator('.c-permission-prompt').first();
        await prompt.waitFor({ state: 'visible', timeout: 10_000 });
        expect(await prompt.innerText()).toContain('Enable location permission to show map');
        expect(await modal.locator('.c-map').count()).toBe(0);
        expect(await modal.locator('.c-map-menu').count()).toBe(0);

        // assert — the share actions stay available: they request the permission themselves
        await modal.locator('.c-send-current').first().waitFor({ state: 'visible', timeout: 5_000 });
        await modal.locator('.c-share-live').first().waitFor({ state: 'visible', timeout: 5_000 });
        await page.screenshot({ path: shot('loc-permission-prompt') });

        // act — grant the permission and click the prompt
        await context.grantPermissions(['geolocation'], { origin: BASE_URL });
        await context.setGeolocation(START);
        await prompt.click();

        // assert — the open modal swaps the prompt for a real map with the own marker
        await prompt.waitFor({ state: 'hidden', timeout: 30_000 });
        await modal.locator('.c-map').first().waitFor({ state: 'visible', timeout: 20_000 });
        await modal.locator('.c-map-menu').first().waitFor({ state: 'visible', timeout: 10_000 });
        await modal.locator('.maplibregl-marker').first().waitFor({ state: 'visible', timeout: 20_000 });
        await page.waitForTimeout(1_500); // let the tiles paint before the screenshot
        await page.screenshot({ path: shot('loc-permission-granted') });

        await page.keyboard.press('Escape');
        await modal.waitFor({ state: 'hidden', timeout: 10_000 }).catch(() => { /* ignore */ });
    }, 180_000);
});
