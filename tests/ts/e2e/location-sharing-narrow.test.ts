/**
 * E2E test: the chat activity panel at phone width while only sharing location (#4088).
 *
 * A map-only share shows the panel reduced to its stop-sharing button. It must NOT
 * pick up the narrow `not-participating` styling — that's the bordered "Join muted"
 * box, and with every call-side control suppressed it renders as a big empty outline.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/location-sharing-narrow.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Page } from 'playwright';
import {
    BASE_URL, clearBrowserCache, connectBrowser, ensureSignedIn, screenshot, skipOnboarding,
    waitForChatReady, waitForEditor, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e', name);

const CHAT_URL = `${BASE_URL}/chat/the-actual-one`;
const START = { latitude: 51.5074, longitude: -0.1278, accuracy: 12 };
const PHONE_VIEWPORT = { width: 390, height: 844 };

describe('location sharing (narrow)', () => {
    let conn: BrowserConnection;
    let page: Page;

    beforeAll(async () => {
        conn = await connectBrowser();
        await conn.context.grantPermissions(['geolocation'], { origin: BASE_URL });
        await conn.context.setGeolocation(START);
        page = await conn.context.newPage();
        await clearBrowserCache(page);
        await ensureSignedIn(page);
    }, 120_000);

    afterAll(async () => {
        await page.close().catch(() => { /* ignore */ });
        if (conn.ownsBrowser) {
            await conn.context.close().catch(() => { /* ignore */ });
            await conn.browser.close().catch(() => { /* ignore */ });
        }
    });

    it('shows a compact activity panel for a map-only share, not the join-muted box', async () => {
        // arrange — go narrow BEFORE navigating: resizing after load flips PanelsUI to the
        // left panel, leaving the chat (and its editor) off screen
        await page.setViewportSize(PHONE_VIEWPORT);
        await page.goto(CHAT_URL, { waitUntil: 'domcontentloaded' });
        await waitForChatReady(page);
        await skipOnboarding(page);

        const joinButton = page.locator('button:has-text("Join this chat")');
        if (await joinButton.isVisible({ timeout: 3000 }).catch(() => false)) {
            await joinButton.click();
            await page.waitForTimeout(1500);
        }
        await waitForEditor(page);

        // act — start a live share with no call activity
        await page.locator('.chat-message-editor .attach-btn').first().click({ force: true });
        await page.locator('.ac-menu-item:has-text("Location")').first().click({ force: true });
        const modal = page.locator('.share-location-modal').first();
        await modal.waitFor({ state: 'visible', timeout: 10_000 });
        await modal.locator('.c-share-live').first().click();
        await modal.locator('.c-duration-menu .c-menu-item:has-text("15 minutes")').first().click();

        // assert — the panel appears in its map-only form with the stop button
        const panel = page.locator('.chat-activity-panel:has(.btn-stop-location-sharing)').first();
        await panel.waitFor({ state: 'visible', timeout: 20_000 });
        await panel.locator('.btn-stop-location-sharing').first()
            .waitFor({ state: 'visible', timeout: 10_000 });

        // assert — no call-side affordances: neither the join-muted box styling nor its button
        expect(await page.locator('.chat-activity-panel.not-participating').count()).toBe(0);
        expect(await panel.locator('.not-listening').count()).toBe(0);
        expect(await panel.locator('.btn-h.talking').count()).toBe(0);
        await page.screenshot({ path: shot('narrow-map-only') });

        // act + assert — the stop button ends the share and the panel goes with it
        await panel.locator('.btn-stop-location-sharing').first().click();
        await panel.waitFor({ state: 'hidden', timeout: 20_000 });
        await page.locator('.visual-activity-panel .map-panel').first()
            .waitFor({ state: 'hidden', timeout: 20_000 }).catch(() => { /* ignore */ });
    }, 180_000);
});
