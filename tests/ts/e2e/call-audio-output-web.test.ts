/**
 * E2E test: the call screen's audio-output button on the web.
 *
 * A browser offers nothing to route call audio between - the base AudioFocusUI publishes no
 * output routes - so the call screen must show no speaker button at all, on the caller's side
 * and on the callee's. Two users at a phone-sized viewport (an audio call has its screen only on
 * the narrow layout): one dials from the peer chat, the other accepts from the incoming-call
 * modal, both reach the call screen's control bar.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch), locally: calls are incomplete UI, which the test
 *   turns on for both accounts - test agents are admins only on a local server.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/call-audio-output-web.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import type { BrowserContext, Page } from 'playwright';
import {
    TEST_EMAIL, TEST_EMAIL_2, connectBrowser, ensureSignedIn, screenshot, setIncompleteUI,
    type BrowserConnection,
} from './helpers';
import { CALL_HANG_UP, CALL_SCREEN, openPeerChat } from './peer-call';

const shot = (name: string) => screenshot('e2e-call', name);

async function newPhoneContext(
    conn: BrowserConnection,
    email: string,
): Promise<{ context: BrowserContext; page: Page }> {
    // No touch: the ring buttons attach a swipe-to-answer controller on touch devices, and the
    // toolbar is what's under test, not the swipe.
    const context = await conn.browser.newContext({
        ignoreHTTPSErrors: true,
        locale: 'en-US',
        viewport: { width: 390, height: 844 },
        deviceScaleFactor: 2,
        isMobile: true,
        hasTouch: false,
        permissions: ['microphone'],
    });
    const page = await context.newPage();
    page.on('pageerror', e => console.log(`PAGEERROR[${email}]:`, e.message));
    await ensureSignedIn(page, email);
    return { context, page };
}

async function hangUpIfAny(page: Page | undefined) {
    if (!page)
        return;

    const hangUp = page.locator(CALL_HANG_UP).first();
    if (await hangUp.isVisible({ timeout: 1_000 }).catch(() => false))
        await hangUp.click().catch(() => { /* ignore */ });
}

describe('call audio output on the web', () => {
    let conn: BrowserConnection;
    let callerCtx: BrowserContext;
    let calleeCtx: BrowserContext;
    let caller: Page;
    let callee: Page;

    beforeAll(async () => {
        conn = await connectBrowser();
        // Sequential sign-ins: parallel ones race on the shared server flow (see vitest.config.e2e.ts).
        ({ context: callerCtx, page: caller } = await newPhoneContext(conn, TEST_EMAIL));
        ({ context: calleeCtx, page: callee } = await newPhoneContext(conn, TEST_EMAIL_2));
        for (const page of [caller, callee])
            await setIncompleteUI(page, true);
    }, 180_000);

    afterEach(async () => {
        await hangUpIfAny(caller);
        await hangUpIfAny(callee);
    }, 30_000);

    afterAll(async () => {
        for (const page of [caller, callee])
            await setIncompleteUI(page, false).catch(() => { /* ignore */ });
        await callerCtx.close().catch(() => { /* ignore */ });
        await calleeCtx.close().catch(() => { /* ignore */ });
        if (conn.ownsBrowser) {
            await conn.context.close().catch(() => { /* ignore */ });
            await conn.browser.close().catch(() => { /* ignore */ });
        }
    });

    it('shows no speaker button on either side of a call', async () => {
        // arrange - a reply from the callee opens the peer-call gate for the caller
        await openPeerChat(caller, callee);

        // act - dial, accept from the incoming-call modal
        const callButton = caller.locator('.btn-start-call').first();
        await callButton.waitFor({ state: 'visible', timeout: 30_000 });
        await callButton.click();
        await caller.locator(CALL_SCREEN).first().waitFor({ state: 'visible', timeout: 20_000 });
        await caller.screenshot({ path: shot('caller-dialing') });
        const accept = callee.locator('.btn-call.c-accept').first();
        await accept.waitFor({ state: 'visible', timeout: 30_000 });
        await callee.screenshot({ path: shot('callee-ringing') });
        await accept.click();

        // assert - both control bars carry the fixed buttons and no output button
        for (const [who, page] of [['caller', caller], ['callee', callee]] as const) {
            const controls = page.locator('.call-screen.in-call .call-screen-footer').first();
            await controls.waitFor({ state: 'visible', timeout: 30_000 });
            await page.waitForTimeout(1_000);
            await page.screenshot({ path: shot(`${who}-in-call`) });
            expect(await controls.locator('.btn-speaker').count(), `${who}: a browser has no outputs to pick from`)
                .toBe(0);
            for (const button of ['.btn-video-toggle', '.recorder-wrapper'])
                expect(await controls.locator(button).count(), `${who}: ${button}`).toBe(1);
            expect(await page.locator(`${CALL_SCREEN} .call-screen-header .btn-video-menu`).count(), `${who}: ⋮`)
                .toBe(1);
        }
    }, 120_000);
});
