/**
 * E2E test: the call screen's audio-output button on the web.
 *
 * A browser offers nothing to route call audio between - the base AudioFocusUI publishes no
 * output routes - so the full-screen call view must show no speaker button at all, on the
 * caller's side and on the callee's. Two users at a phone-sized viewport (the full-screen
 * view exists only on the narrow layout): one dials from the peer chat, the other accepts
 * from the incoming-call modal, both reach the in-call toolbar.
 *
 * Prerequisites:
 * - Server running (server-loop / run-watch).
 *
 * Run:
 *   npx vitest run tests/ts/e2e/call-audio-output-web.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import type { BrowserContext, Page } from 'playwright';
import {
    BASE_URL, TEST_EMAIL, TEST_EMAIL_2, connectBrowser, ensureSignedIn, screenshot, withUILanguage,
    type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e-call', name);

const TOOLBAR = '.full-screen-call-view.in-call .c-toolbar';

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

async function send(page: Page, text: string) {
    // Focused, not clicked: the record button overlaps the editor on the narrow layout.
    const editor = page.locator('#message-input .editor-content[contenteditable="true"]').first();
    await editor.waitFor({ state: 'visible', timeout: 30_000 });
    await editor.focus();
    await page.keyboard.type(text, { delay: 20 });
    await page.keyboard.press('Enter');
    await page.waitForTimeout(1_500);
}

async function getUserId(page: Page): Promise<string> {
    return await page.evaluate(() => (window as unknown as { debugUI: { getUserId(): Promise<string> } })
        .debugUI.getUserId());
}

async function hangUpIfAny(page: Page | undefined) {
    if (!page)
        return;

    const hangUp = page.locator('.full-screen-call-view .c-call-bar .btn-video-panel.talking').first();
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
    }, 180_000);

    afterEach(async () => {
        await hangUpIfAny(caller);
        await hangUpIfAny(callee);
    }, 30_000);

    afterAll(async () => {
        await callerCtx.close().catch(() => { /* ignore */ });
        await calleeCtx.close().catch(() => { /* ignore */ });
        if (conn.ownsBrowser) {
            await conn.context.close().catch(() => { /* ignore */ });
            await conn.browser.close().catch(() => { /* ignore */ });
        }
    });

    it('shows no speaker button on either side of a call', async () => {
        // arrange - a reply from the callee opens the peer-call gate for the caller
        const userIds = [await getUserId(caller), await getUserId(callee)].sort();
        const pm = withUILanguage(`${BASE_URL}/chat/p-${userIds.join('-')}`);
        await caller.goto(pm, { waitUntil: 'domcontentloaded' });
        await send(caller, 'call test');
        await callee.goto(pm, { waitUntil: 'domcontentloaded' });
        await send(callee, 'call test reply');
        await caller.reload({ waitUntil: 'domcontentloaded' });

        // act - dial, accept from the incoming-call modal
        const callButton = caller.locator('.btn-start-call').first();
        await callButton.waitFor({ state: 'visible', timeout: 30_000 });
        await callButton.click();
        await caller.locator('.full-screen-call-view').first().waitFor({ state: 'visible', timeout: 20_000 });
        await caller.screenshot({ path: shot('caller-dialing') });
        const accept = callee.locator('.btn-call.c-accept').first();
        await accept.waitFor({ state: 'visible', timeout: 30_000 });
        await callee.screenshot({ path: shot('callee-ringing') });
        await accept.click();

        // assert - both in-call toolbars carry the four fixed buttons and no output button
        for (const [who, page] of [['caller', caller], ['callee', callee]] as const) {
            const toolbar = page.locator(TOOLBAR).first();
            await toolbar.waitFor({ state: 'visible', timeout: 30_000 });
            await page.waitForTimeout(1_000);
            await page.screenshot({ path: shot(`${who}-in-call`) });
            expect(await toolbar.locator('.c-speaker').count(), `${who}: a browser has no outputs to pick from`)
                .toBe(0);
            expect(await toolbar.locator('.btn-video-panel').count(), `${who}: share, video, record, options`)
                .toBe(4);
        }
    }, 120_000);
});
