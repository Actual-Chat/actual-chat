/**
 * E2E test: live location sharing.
 *
 * Drives the full web flow with a mocked browser geolocation:
 *   "+" menu -> Share location -> pick a duration -> the inline map panel appears
 *   at the top of the chat (same slot as the video call panel, #4067) ->
 *   expand it into the map modal (a MapLibre marker renders) -> move the mock
 *   position -> Stop -> the panel disappears.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch).
 * - Optionally, Chrome with remote debugging: `ai chrome` (port 9222) to watch live.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/location-sharing.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import type { Page } from 'playwright';
import {
    BASE_URL, clearBrowserCache, connectBrowser, ensureSignedIn, skipOnboarding, screenshot,
    waitForChatReady, waitForEditor, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e', name);

// A shared chat the test user can join, so it has an editor + a panel host to share from.
const CHAT_URL = `${BASE_URL}/chat/the-actual-one`;

// London -> Paris, to prove the marker tracks position updates.
const START = { latitude: 51.5074, longitude: -0.1278, accuracy: 12 };
const MOVED = { latitude: 48.8566, longitude: 2.3522, accuracy: 12 };

// An ACTUAL tile/glyph fetch from our maps.* proxy (not just the style JSON).
const TILE_URL_RE = /maps[.-][^/]*\.(?:voxt\.ai|actual\.chat)\/(?:planet\/.*\.pbf|natural_earth\/.*\.png|fonts\/)/;

// A failed test must not leak its live share into the next one: re-sharing over an
// active share mints a NEW SharedLocation and orphans the old row server-side, where
// nothing can stop it until it expires on its own.
async function stopSharingIfAny(page: Page) {
    for (let i = 0; i < 2; i++) {
        await page.keyboard.press('Escape').catch(() => { /* ignore */ });
        await page.waitForTimeout(300);
    }
    // A failed hide-flow test leaves the panel collapsed to the pill — restore it first.
    const pill = page.locator('.activity-pill').first();
    if (await pill.isVisible({ timeout: 500 }).catch(() => false)) {
        await pill.click().catch(() => { /* ignore */ });
        await page.waitForTimeout(500);
    }
    const stopButton = page.locator('.visual-activity-panel .map-panel .btn-stop-sharing').first();
    if (await stopButton.isVisible({ timeout: 1_000 }).catch(() => false)) {
        await stopButton.click().catch(() => { /* ignore */ });
        await page.locator('.visual-activity-panel .map-panel').first()
            .waitFor({ state: 'hidden', timeout: 15_000 }).catch(() => { /* ignore */ });
    }
}

describe('location sharing', () => {
    let conn: BrowserConnection;
    let page: Page;

    beforeAll(async () => {
        conn = await connectBrowser();
        await conn.context.grantPermissions(['geolocation'], { origin: BASE_URL });
        await conn.context.setGeolocation(START);
        page = await conn.context.newPage();
        await clearBrowserCache(page);
        page.on('pageerror', e => console.log('PAGEERROR:', e.message));
        page.on('console', m => {
            if (m.type() === 'error')
                console.log('CONSOLE.ERROR:', m.text());
        });
        await ensureSignedIn(page);
    }, 120_000);

    afterEach(async () => {
        await stopSharingIfAny(page);
    }, 30_000);

    afterAll(async () => {
        await page.close();
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    });

    it('shares location, shows the inline map panel + map marker, then stops', async () => {
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

        // act — open the "+" menu and start a share (arm the tile listener BEFORE the share
        // starts: the inline panel begins fetching tiles the moment it mounts)
        const tileLoaded = page.waitForResponse(
            r => TILE_URL_RE.test(r.url()) && r.ok(),
            { timeout: 30_000 });
        await page.locator('.chat-message-editor .attach-btn').first().click({ force: true });
        await page.locator('.ac-menu-item:has-text("Location")').first().click({ force: true });

        const modal = page.locator('.share-location-modal').first();
        await modal.waitFor({ state: 'visible', timeout: 10_000 });
        await page.screenshot({ path: shot('loc-modal') });
        await modal.locator('.c-share-live').first().click();
        await modal.locator('.c-duration-menu .c-menu-item:has-text("15 minutes")').first().click();

        // assert — the inline map panel appears in the video-panel slot (#4067) with the
        // own-share affordances: stop button + countdown ring, no share CTA
        const mapPanel = page.locator('.visual-activity-panel .map-panel').first();
        await mapPanel.waitFor({ state: 'visible', timeout: 20_000 });
        await mapPanel.locator('.btn-stop-sharing').first().waitFor({ state: 'visible', timeout: 10_000 });
        await mapPanel.locator('.c-countdown').first().waitFor({ state: 'visible', timeout: 10_000 });
        expect(await mapPanel.locator('.btn-share-location').count()).toBe(0);

        // assert — with only the map activity (no call) the Call/Map switch is hidden (#4067)
        expect(await page.locator('.call-map-switch').count()).toBe(0);

        // assert — the chat activity panel shows for the map-only share, reduced to the
        // stop-sharing button (#4088)
        const activityPanel = page.locator('.chat-activity-panel:has(.btn-stop-location-sharing)').first();
        await activityPanel.waitFor({ state: 'visible', timeout: 10_000 });
        await activityPanel.locator('.btn-stop-location-sharing').first()
            .waitFor({ state: 'visible', timeout: 10_000 });

        // assert — the activity pill appears only while the panel is hidden
        expect(await page.locator('.activity-pill').count()).toBe(0);

        // assert — the inline panel renders a MapLibre marker on real tiles (not blank: CSP
        // allows the maps host AND the map got a non-zero viewport)
        await mapPanel.locator('.maplibregl-marker').first().waitFor({ state: 'visible', timeout: 15_000 });
        const tileResp = await tileLoaded;
        expect(tileResp.ok()).toBe(true);
        await page.waitForTimeout(1_500); // let the tiles paint before the screenshot
        await page.screenshot({ path: shot('loc-panel') });

        // act — hide the panel from its ⋮ menu; the activity pill takes its place (#4088)
        await mapPanel.locator('.c-menu-btn').first().click();
        const panelMenu = page.locator('.map-panel-host .c-menu').first();
        await panelMenu.waitFor({ state: 'visible', timeout: 5_000 });
        await panelMenu.locator('.c-menu-item:has-text("Hide")').first().click();
        await mapPanel.waitFor({ state: 'hidden', timeout: 10_000 });
        const pill = page.locator('.activity-pill').first();
        await pill.waitFor({ state: 'visible', timeout: 10_000 });
        expect((await pill.innerText()).toLowerCase()).toContain('live');
        // The own share adds its marker-pin icon to the pill
        await pill.locator('i.icon-marker-pin').first().waitFor({ state: 'visible', timeout: 10_000 });
        await page.screenshot({ path: shot('loc-panel-hidden') });

        // act — clicking the pill brings the panel back
        await pill.click();
        await mapPanel.waitFor({ state: 'visible', timeout: 10_000 });
        await pill.waitFor({ state: 'hidden', timeout: 10_000 });
        await mapPanel.locator('.maplibregl-marker').first().waitFor({ state: 'visible', timeout: 15_000 });

        // act — expand the panel into the full map modal
        await mapPanel.locator('.c-expand').first().click();
        const mapModal = page.locator('.location-map-modal').first();
        await mapModal.waitFor({ state: 'visible', timeout: 10_000 });

        // assert — a MapLibre marker renders for the share
        const marker = mapModal.locator('.maplibregl-marker').first();
        await marker.waitFor({ state: 'visible', timeout: 15_000 });

        // assert — the participants list shows the sharer: own row marked "(you)"
        // with a live "Updated ..." status
        const participant = mapModal.locator('.c-participant').first();
        await participant.waitFor({ state: 'visible', timeout: 10_000 });
        expect(await mapModal.locator('.c-participant').count()).toBe(1);
        const participantText = (await participant.innerText()).toLowerCase();
        expect(participantText).toContain('(you)');
        expect(participantText).toContain('updated');

        // assert — no attribution control (MapLibre | OpenFreeMap ... info bar) is drawn
        expect(await mapModal.locator('.maplibregl-ctrl-attrib').count()).toBe(0);
        await page.waitForTimeout(1_500); // let the tiles paint before the screenshot
        await page.screenshot({ path: shot('loc-map') });

        // act — move the position; the marker should track it (still exactly one)
        await conn.context.setGeolocation(MOVED);
        await page.waitForTimeout(2_000);
        expect(await mapModal.locator('.maplibregl-marker').count()).toBe(1);
        await page.screenshot({ path: shot('loc-map-moved') });

        // act — close the map, then stop sharing from the inline panel
        await page.keyboard.press('Escape');
        await mapModal.waitFor({ state: 'hidden', timeout: 10_000 }).catch(() => { /* ignore */ });
        await mapPanel.locator('.btn-stop-sharing').first().click();

        // assert — the panel and the activity strip disappear once the share is stopped
        await mapPanel.waitFor({ state: 'hidden', timeout: 20_000 });
        await activityPanel.waitFor({ state: 'hidden', timeout: 10_000 });
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
        await page.locator('.ac-menu-item:has-text("Location")').first().click({ force: true });

        const modal = page.locator('.share-location-modal').first();
        await modal.waitFor({ state: 'visible', timeout: 10_000 });

        // arm the tile listener before posting so we don't miss the inline-map tile requests
        const tileLoaded = page.waitForResponse(
            r => TILE_URL_RE.test(r.url()) && r.ok(),
            { timeout: 20_000 });
        await modal.locator('.c-send-current').first().click();

        // assert — a one-shot location message appears in the chat stream with an inline map marker
        const locationMessage = page.locator('.location-message').last();
        await locationMessage.waitFor({ state: 'visible', timeout: 20_000 });
        const marker = locationMessage.locator('.maplibregl-marker').first();
        await marker.waitFor({ state: 'visible', timeout: 15_000 });

        // assert — the marker is the avatar bubble (sender's avatar inside the circle),
        // not the generic no-avatar pin (#4057)
        await marker.locator('map-marker-bubble .c-avatar').first()
            .waitFor({ state: 'visible', timeout: 15_000 });
        expect(await marker.locator('map-marker-pin').count()).toBe(0);

        // assert — the inline map actually paints real tiles (not blank)
        const tileResp = await tileLoaded;
        expect(tileResp.ok()).toBe(true);
        await page.waitForTimeout(1_500); // let the tiles paint before the screenshot
        await page.screenshot({ path: shot('loc-one-shot') });

        // assert — the chat-list preview summarizes the location entry instead of rendering blank (#4028).
        // A location entry has empty text Content, so without the fallback the "chat news" line is empty.
        // Select the row by title: the current chat's row carries no data-href (NavbarItem drops it).
        const listRow = page.locator('.chat-list .navbar-item', { hasText: 'The Actual One' }).first();
        const listPreview = listRow.locator('.c-last-message .c-text:has-text("location")');
        await listPreview.waitFor({ state: 'visible', timeout: 15_000 });
        expect(await listRow.locator('.c-last-message .c-text i.icon-map-point').count()).toBe(1);

        // assert — there is no live-share map panel for a one-shot send (it's a static message)
        const mapPanel = page.locator('.visual-activity-panel .map-panel').first();
        expect(await mapPanel.isVisible({ timeout: 2_000 }).catch(() => false)).toBe(false);

        // assert — the inline map is NOT interactive (no MapLibre interaction handlers attached)
        expect(await locationMessage.locator('.maplibregl-interactive').count()).toBe(0);

        // act — clicking the message opens the interactive map modal
        await locationMessage.click();
        const mapModal = page.locator('.location-map-modal').first();
        await mapModal.waitFor({ state: 'visible', timeout: 10_000 });

        // assert — the modal map is interactive and shows the location's marker
        await mapModal.locator('.maplibregl-marker').first().waitFor({ state: 'visible', timeout: 15_000 });
        expect(await mapModal.locator('.maplibregl-interactive').count()).toBeGreaterThan(0);

        // assert — the one-shot sender appears in the participants list with an update time
        const senderRow = mapModal.locator('.c-participant').first();
        await senderRow.waitFor({ state: 'visible', timeout: 10_000 });
        const senderText = (await senderRow.innerText()).toLowerCase();
        expect(senderText).toContain('updated');
        await page.screenshot({ path: shot('loc-one-shot-modal') });
        await page.keyboard.press('Escape');
        await mapModal.waitFor({ state: 'hidden', timeout: 10_000 }).catch(() => { /* ignore */ });
    }, 180_000);

    it('shows the stop-sharing button in the chat activity panel while sharing (#4088)', async () => {
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

        // arrange — start recording (fake mic): the chat activity panel (the hang-up row
        // the stop-sharing button sits in) only renders during call activity
        await conn.context.grantPermissions(['geolocation', 'microphone'], { origin: BASE_URL });
        await page.locator('.recorder-wrapper button').first().click();
        const activityPanel = page.locator('.chat-activity-panel').first();
        await activityPanel.waitFor({ state: 'visible', timeout: 20_000 });
        expect(await activityPanel.locator('.btn-stop-location-sharing').count()).toBe(0);

        try {
            // act — start a live share
            await page.locator('.chat-message-editor .attach-btn').first().click({ force: true });
            await page.locator('.ac-menu-item:has-text("Location")').first().click({ force: true });
            const modal = page.locator('.share-location-modal').first();
            await modal.waitFor({ state: 'visible', timeout: 10_000 });
            await modal.locator('.c-share-live').first().click();
            await modal.locator('.c-duration-menu .c-menu-item:has-text("15 minutes")').first().click();
            const mapPanel = page.locator('.visual-activity-panel .map-panel').first();
            await mapPanel.waitFor({ state: 'visible', timeout: 20_000 });

            // assert — the stop button appears in the activity panel's button row,
            // next to the audio-diagnostics and hang-up buttons
            const stopButton = activityPanel.locator('.btn-stop-location-sharing').first();
            await stopButton.waitFor({ state: 'visible', timeout: 10_000 });
            await page.screenshot({ path: shot('loc-activity-stop-btn') });

            // act + assert — clicking it stops the share: the map panel and the button both go
            await stopButton.click();
            await mapPanel.waitFor({ state: 'hidden', timeout: 20_000 });
            await expect.poll(
                async () => activityPanel.locator('.btn-stop-location-sharing').count(),
                { timeout: 10_000 },
            ).toBe(0);
        } finally {
            // cleanup — hang up so the next test starts without call activity
            await activityPanel.locator('.btn-h.talking').first().click().catch(() => { /* ignore */ });
            await activityPanel.waitFor({ state: 'hidden', timeout: 10_000 }).catch(() => { /* ignore */ });
        }
    }, 120_000);

    // Every duration option must start a live share (the inline map panel appears), not just
    // the first one. The countdown ring text is duration-specific: minutes for sub-hour
    // shares, "Nh" for hour-scale ones ("1 hour" may render as "60" or "1h" depending on
    // sub-second clock skew).
    for (const [label, countdownRe] of [
        ['15 minutes', /^15$/],
        ['1 hour', /^(60|1h)$/],
        ['8 hours', /^8h$/],
    ] as const) {
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
            await page.locator('.ac-menu-item:has-text("Location")').first().click({ force: true });

            const modal = page.locator('.share-location-modal').first();
            await modal.waitFor({ state: 'visible', timeout: 10_000 });
            await modal.locator('.c-share-live').first().click();
            await modal.locator(`.c-duration-menu .c-menu-item:has-text("${label}")`).first().click();

            // assert — the map panel reflects the active share for this duration, and appears promptly.
            // The report loop must wait for the tracker's first fix before its first cycle; if it runs
            // once with an empty LastKnown it posts nothing and the share only starts a full UpdatePeriod
            // (10s) later — the "doesn't start on the first try" bug. Keep the timeout under UpdatePeriod.
            const mapPanel = page.locator('.visual-activity-panel .map-panel').first();
            await mapPanel.waitFor({ state: 'visible', timeout: 8_000 });

            // A freshly picked share must show its full remaining time — e.g. 15 minutes reads
            // "15", not "16": the countdown rounds the minute up, so sub-minute clock skew has
            // to be absorbed first.
            const countdown = mapPanel.locator('.c-countdown').first();
            await countdown.waitFor({ state: 'visible', timeout: 10_000 });
            expect((await countdown.innerText()).trim()).toMatch(countdownRe);

            // cleanup — stop the share so the next duration starts clean
            await mapPanel.locator('.btn-stop-sharing').first().click();
            await mapPanel.waitFor({ state: 'hidden', timeout: 20_000 });
        }, 120_000);
    }
});
