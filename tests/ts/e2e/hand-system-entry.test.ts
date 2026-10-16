/**
 * E2E test: a hand going up or down in a call leaves a system line in the chat (#5391).
 *
 * Two users (Alice, Bob) turn their cameras on in the same chat, which starts a live session.
 * Bob raises his hand, Alice (the host) lowers it from the Call tab, then Bob raises it again and
 * lowers it himself. Each move shows as a grey system line in the chat, on both sides, naming whose
 * hand moved - so the chat tells the story to anyone who looks at it later.
 *
 * Screenshots go to tmp/e2e-hand-system-entry-*.png.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch), locally: hands are incomplete UI, which the test
 *   turns on for both accounts - test agents are admins only on a local server.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/hand-system-entry.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import type { BrowserContext, Page } from 'playwright';
import {
    BASE_URL, TEST_EMAIL, TEST_EMAIL_2, connectBrowser, newUserContext, screenshot, setIncompleteUI,
    type BrowserConnection,
} from './helpers';
import {
    SPEECH_WAV, collapseVideoPanel, expandVideoPanel, hangUpIfAny, openCallChat, openCallTab,
    openReactionsMenu, startSession, stopRecording,
} from './video-call';

const shot = (name: string) => screenshot('e2e-hand-system-entry', name);

const HAND_LINES = /(raised|lowered) a hand/;

/** The hand lines on screen, newest last - the chat drops older lines as it scrolls, so only the tail counts. */
async function getHandLines(page: Page): Promise<string[]> {
    const lines = await page.locator('.chat-view .system-entry .chat-message-markup')
        .filter({ hasText: HAND_LINES }).allInnerTexts();
    return lines.map(x => x.replace(/\s+/g, ' ').trim());
}

/** Bob's own hand: the React button in the expanded footer toggles it, lit while it's up. */
async function toggleOwnHand(page: Page) {
    await expandVideoPanel(page);
    const menu = await openReactionsMenu(page, 20_000);
    expect(menu, 'the call offers reactions').not.toBeNull();
    await menu!.locator('.ac-menu-item:has(.icon-hand)').first().click();
    await collapseVideoPanel(page);
}

describe('hand system entry', () => {
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

    it('every raise and lower leaves a line in the chat naming whose hand moved', async () => {
        // arrange - Alice starts first, so she is the host
        await openCallChat(alice);
        await openCallChat(bob);
        await startSession(alice, bob);
        const bobName = (await alice.locator('.call-screen .remote-video-container .video-participant-label span')
            .first().innerText()).trim();
        expect(bobName.length).toBeGreaterThan(0);
        // The fake mic never stops talking, and a hand lowers itself once its owner says a few words
        await stopRecording(bob);
        const linesBefore = (await getHandLines(alice)).length;

        // act - Bob raises his hand
        await toggleOwnHand(bob);

        // assert - both chats get the line, and it names Bob
        for (const page of [alice, bob])
            await expect.poll(() => getHandLines(page), { timeout: 15_000 })
                .toHaveLength(linesBefore + 1);
        expect((await getHandLines(alice)).at(-1)).toBe(`${bobName} raised a hand.`);
        await alice.screenshot({ path: shot('1-raised-alice') });
        await bob.screenshot({ path: shot('2-raised-bob') });

        // act - Alice, the host, lowers Bob's hand from the Call tab
        await openCallTab(alice);
        const lowerButton = alice.locator('.live-session-member-list .btn-lower-hand').first();
        await lowerButton.waitFor({ state: 'visible', timeout: 15_000 });
        await lowerButton.click();

        // assert - the line still names Bob: it's his hand, whoever put it down
        for (const page of [alice, bob])
            await expect.poll(() => getHandLines(page), { timeout: 15_000 })
                .toHaveLength(linesBefore + 2);
        expect((await getHandLines(bob)).at(-1)).toBe(`${bobName} lowered a hand.`);
        await alice.screenshot({ path: shot('3-lowered-by-host-alice') });
        await bob.screenshot({ path: shot('4-lowered-by-host-bob') });

        // act - Bob raises it again and lowers it himself
        await toggleOwnHand(bob);
        await expect.poll(() => getHandLines(bob), { timeout: 15_000 }).toHaveLength(linesBefore + 3);
        await toggleOwnHand(bob);

        // assert
        for (const page of [alice, bob])
            await expect.poll(() => getHandLines(page), { timeout: 15_000 })
                .toHaveLength(linesBefore + 4);
        expect((await getHandLines(alice)).slice(-4)).toEqual([
            `${bobName} raised a hand.`,
            `${bobName} lowered a hand.`,
            `${bobName} raised a hand.`,
            `${bobName} lowered a hand.`,
        ]);
        await alice.screenshot({ path: shot('5-full-story-alice') });

        // assert - the lines outlive the call: the chat tells the story to whoever looks later
        await hangUpIfAny(bob);
        await hangUpIfAny(alice);
        const lastLine = alice.locator('.chat-view .system-entry').filter({ hasText: HAND_LINES }).last();
        await lastLine.scrollIntoViewIfNeeded();
        await alice.waitForTimeout(1_000);
        expect((await getHandLines(alice)).slice(-4)).toHaveLength(4);
        await alice.screenshot({ path: shot('6-after-the-call-alice') });
    }, 300_000);
});
