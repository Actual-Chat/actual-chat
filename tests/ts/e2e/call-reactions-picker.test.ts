/**
 * E2E test: any emoji as a call reaction (#4918).
 *
 * Bob opens the React menu in a video call with Alice. The menu starts as the six quick emojis plus
 * a chevron; the chevron expands it to the whole picker, and an emoji from there (💩 - never in the
 * old preset) floats over Alice's video panel. Once on a wide viewport, once on a phone-sized one,
 * with a screenshot of each menu state.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch), locally: hands and reactions are incomplete UI, which
 *   the test turns on for both accounts - test agents are admins only on a local server.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/call-reactions-picker.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import type { BrowserContext, Locator, Page } from 'playwright';
import {
    BASE_URL, TEST_EMAIL, TEST_EMAIL_2, connectBrowser, newUserContext, screenshot, setIncompleteUI,
    type BrowserConnection,
} from './helpers';
import { SPEECH_WAV, expandVideoPanel, hangUpIfAny, openCallChat, startSession } from './video-call';

const shot = (name: string) => screenshot('e2e-call-reactions', name);

const PHONE_VIEWPORT = { width: 390, height: 844 };
const QUICK_EMOJI_COUNT = 6;
const PICKED_EMOJI_TITLE = 'Pile of poo';

describe('call reactions picker', () => {
    let conn: BrowserConnection;
    let aliceCtx: BrowserContext;
    let bobCtx: BrowserContext;
    let alice: Page;
    let bob: Page;

    beforeAll(async () => {
        conn = await connectBrowser({ fakeAudioFile: SPEECH_WAV });
        // Sequential sign-ins: parallel ones race on the shared server flow (see vitest.config.e2e.ts).
        ({ context: aliceCtx, page: alice } = await newUserContext(conn, TEST_EMAIL));
        ({ context: bobCtx, page: bob } = await newUserContext(conn, TEST_EMAIL_2));
        // The fake-device flags answer getUserMedia, but the app reads the Permissions API first
        // and shows "No microphone access" while a fresh context still reports "prompt".
        for (const ctx of [aliceCtx, bobCtx])
            await ctx.grantPermissions(['microphone', 'camera'], { origin: BASE_URL });
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

    it('wide: the chevron opens the whole picker and any emoji floats on the other side', async () => {
        await openCallChat(alice);
        await openCallChat(bob);
        await startSession(alice, bob);
        await pickFromFullPicker('wide');
    }, 300_000);

    it('narrow: the same picker in the bottom-sheet menu', async () => {
        // Go narrow BEFORE navigating: resizing after load flips PanelsUI to the narrow layout,
        // but the in-call chrome is laid out once per page.
        await bob.setViewportSize(PHONE_VIEWPORT);
        await openCallChat(alice);
        await openCallChat(bob);
        await startSession(alice, bob);
        await pickFromFullPicker('narrow');
    }, 300_000);

    async function pickFromFullPicker(layout: 'wide' | 'narrow') {
        // arrange - Bob expands the panel (the React button lives in its footer) and opens the React
        // menu: six quick emojis and the chevron
        await expandVideoPanel(bob);
        const reactButton = bob.locator('.call-screen .btn-react').first();
        await reactButton.waitFor({ state: 'visible', timeout: 20_000 });
        await reactButton.click();
        const menu = bob.locator('.call-reactions-menu').first();
        await menu.waitFor({ state: 'visible', timeout: 10_000 });
        await expect.poll(async () => menu.locator('.reaction-select-reaction').count()).toBe(QUICK_EMOJI_COUNT);
        await waitForMenuShown(menu);
        await bob.screenshot({ path: shot(`${layout}-1-quick-row`) });

        // act - the chevron expands the menu in place
        await menu.locator('.reaction-select-toggle').first().click();

        // assert - the whole grouped catalog, more than the quick row, and the menu is still open
        const allEmojis = menu.locator('.reactions-menu-item');
        await allEmojis.first().waitFor({ state: 'visible', timeout: 10_000 });
        expect(await allEmojis.count()).toBeGreaterThan(QUICK_EMOJI_COUNT);
        await waitForMenuShown(menu);
        await bob.screenshot({ path: shot(`${layout}-2-full-picker`) });

        // act - an emoji that was never in the old preset
        const picked = allEmojis.filter({ has: bob.locator(`img[alt="${PICKED_EMOJI_TITLE}"]`) }).first();
        await picked.scrollIntoViewIfNeeded();
        await picked.click();

        // assert - the menu closes, and the emoji floats over Alice's panel
        await menu.waitFor({ state: 'hidden', timeout: 10_000 });
        const reaction = alice.locator('.call-screen .call-reactions-overlay .c-reaction').first();
        await reaction.waitFor({ state: 'attached', timeout: 15_000 });
        expect(await reaction.locator('.c-emoji').getAttribute('alt')).toBe(PICKED_EMOJI_TITLE);
        await alice.screenshot({ path: shot(`${layout}-3-reaction-on-other-side`) });
    }
});

/** The menu fades in over 200ms and its emojis are SVG images that arrive one by one, so a screenshot
 *  taken as soon as the entries exist shows a ghost of the menu, or a menu with holes in it. */
async function waitForMenuShown(menu: Locator) {
    await menu.evaluate(async el => {
        const host = el.closest('.ac-menu') ?? el;
        await Promise.all(host.getAnimations({ subtree: true }).map(a => a.finished));
    });
    await expect.poll(async () => menu.locator('img').evaluateAll(
        imgs => imgs.every(img => (img as HTMLImageElement).complete && (img as HTMLImageElement).naturalWidth > 0),
    ), { timeout: 15_000 }).toBe(true);
}
