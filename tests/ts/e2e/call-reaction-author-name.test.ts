/**
 * E2E test: the author name under a floating call reaction is not cut off (#4953).
 *
 * Bob reacts in a video call with Alice. The reaction on Alice's side is then re-labeled with a
 * short, a medium and a very long author name at the panel's leftmost and rightmost reaction
 * positions: a real-life name fits whole, and a name wider than the panel ends with an ellipsis
 * instead of running past the panel's edge. Once on a wide viewport, once on a phone-sized one,
 * with a screenshot per name.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch), locally: hands and reactions are incomplete UI, which
 *   the test turns on for both accounts - test agents are admins only on a local server.
 *
 * Run:
 *   AC_E2E_SERVER=external npx vitest run tests/ts/e2e/call-reaction-author-name.test.ts --config vitest.config.e2e.ts
 */

import * as fs from 'fs';
import * as path from 'path';
import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import type { BrowserContext, Page } from 'playwright';
import {
    BASE_URL, TEST_EMAIL, TEST_EMAIL_2, connectBrowser, newUserContext, setIncompleteUI,
    type BrowserConnection,
} from './helpers';
import { SPEECH_WAV, expandVideoPanel, hangUpIfAny, openCallChat, startSession } from './video-call';

const SHOT_DIR = path.join(process.cwd(), 'tmp', 'e2e-call-reaction-author-name');
const shot = (name: string) => path.join(SHOT_DIR, `${name}.png`);

const PHONE_VIEWPORT = { width: 390, height: 844 };
const NAMES = [
    { kind: '1-short', name: 'Bo', isTruncated: false },
    { kind: '2-medium', name: 'Frol Chizhov', isTruncated: false },
    {
        kind: '3-long',
        name: 'Maximiliana Alexandrovna Wolfeschlegelsteinhausenbergerdorff-Konstantinopolskaya',
        isTruncated: true,
    },
];
/** The extremes of CallReactionsOverlay.GetLeftPercent */
const LEFTMOST_PERCENT = 10;
const RIGHTMOST_PERCENT = 80;
/** Into the float's fully visible stretch (10%-75% of the 3s animation) */
const FROZEN_AT_MS = 600;

interface LabelMetrics {
    overflowsLeft: boolean;
    overflowsRight: boolean;
    isTruncated: boolean;
}

describe('call reaction author name', () => {
    let conn: BrowserConnection;
    let aliceCtx: BrowserContext;
    let bobCtx: BrowserContext;
    let alice: Page;
    let bob: Page;

    beforeAll(async () => {
        fs.mkdirSync(SHOT_DIR, { recursive: true });
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

    it('wide: every name stays inside the panel, only one wider than the panel is truncated', async () => {
        await openCallChat(alice);
        await openCallChat(bob);
        await startSession(alice, bob);
        await checkNames('wide');
    }, 300_000);

    it('narrow: the same on a phone-sized panel', async () => {
        // Go narrow BEFORE navigating: resizing after load flips PanelsUI to the narrow layout,
        // but the in-call chrome is laid out once per page.
        await alice.setViewportSize(PHONE_VIEWPORT);
        await openCallChat(alice);
        await openCallChat(bob);
        await startSession(alice, bob);
        await checkNames('narrow');
    }, 300_000);

    async function checkNames(layout: 'wide' | 'narrow') {
        // arrange - Bob reacts with the first quick emoji, and it floats over Alice's expanded panel
        await expandVideoPanel(alice);
        await expandVideoPanel(bob);
        const reactButton = bob.locator('.call-screen .btn-react').first();
        await reactButton.waitFor({ state: 'visible', timeout: 20_000 });
        await reactButton.click();
        const menu = bob.locator('.call-reactions-menu').first();
        await menu.waitFor({ state: 'visible', timeout: 10_000 });
        await menu.locator('.reaction-select-reaction').first().click();
        await menu.waitFor({ state: 'hidden', timeout: 10_000 });
        const reaction = alice.locator('.call-screen .call-reactions-overlay .c-reaction').first();
        await reaction.waitFor({ state: 'attached', timeout: 15_000 });

        for (const { kind, name, isTruncated } of NAMES) {
            // act - the same reaction with this author name, frozen mid-float: a screenshot at the
            // rightmost position (where the old label ran past the panel), measurements at both extremes
            await showReactionsNamed(alice, name, [RIGHTMOST_PERCENT]);
            await alice.screenshot({ path: shot(`${layout}-${kind}`) });
            const metrics = await showReactionsNamed(alice, name, [LEFTMOST_PERCENT, RIGHTMOST_PERCENT]);

            // assert - inside the panel at either extreme; an ellipsis only on the name wider than
            // the label may be
            for (const m of metrics) {
                expect(m.overflowsLeft, `${kind} overflows left`).toBe(false);
                expect(m.overflowsRight, `${kind} overflows right`).toBe(false);
                expect(m.isTruncated, `${kind} truncated`).toBe(isTruncated);
            }
        }
        await removeShownReactions(alice);
    }
});

/** Replaces the overlay's reactions with copies of the first one at `leftPercents`, named `name`
 *  and frozen mid-float; returns each copy's label metrics in that order. */
async function showReactionsNamed(page: Page, name: string, leftPercents: number[]): Promise<LabelMetrics[]> {
    return page.evaluate(({ name, leftPercents, frozenAtMs }) => {
        const overlay = document.querySelector<HTMLElement>('.call-screen .call-reactions-overlay')!;
        const source = overlay.querySelector<HTMLElement>('.c-reaction:not(.e2e-copy)')!;
        overlay.querySelectorAll('.e2e-copy').forEach(e => e.remove());
        source.style.display = 'none';
        const overlayRect = overlay.getBoundingClientRect();
        return leftPercents.map(leftPercent => {
            const copy = source.cloneNode(true) as HTMLElement;
            copy.classList.add('e2e-copy');
            copy.style.display = '';
            copy.style.setProperty('--left', `${leftPercent}%`);
            const label = copy.querySelector<HTMLElement>('.c-name')!;
            label.textContent = name;
            overlay.appendChild(copy);
            for (const animation of copy.getAnimations()) {
                animation.pause();
                animation.currentTime = frozenAtMs;
            }
            const rect = label.getBoundingClientRect();
            return {
                overflowsLeft: rect.left < overlayRect.left - 0.5,
                overflowsRight: rect.right > overlayRect.right + 0.5,
                isTruncated: label.scrollWidth > label.clientWidth,
            };
        });
    }, { name, leftPercents, frozenAtMs: FROZEN_AT_MS });
}

async function removeShownReactions(page: Page) {
    await page.evaluate(() => {
        const overlay = document.querySelector('.call-screen .call-reactions-overlay');
        overlay?.querySelectorAll('.e2e-copy').forEach(e => e.remove());
        overlay?.querySelectorAll<HTMLElement>('.c-reaction').forEach(e => { e.style.display = ''; });
    });
}
