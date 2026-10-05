/**
 * Shared steps for e2e tests that need a call between two signed-in users in their peer chat:
 * signing both in at a given viewport, dialing and accepting, telling the call screen, the video
 * panel and the chat apart, and hanging up.
 *
 * Calls are incomplete UI, which signInBoth turns on for both accounts - test agents are admins
 * only on a local server, so the tests that use it need one.
 */

import type { BrowserContext, Page } from 'playwright';
import {
    BASE_URL, TEST_EMAIL, TEST_EMAIL_2, connectBrowser, ensureSignedIn, setIncompleteUI, skipOnboarding,
    withUILanguage,
    type BrowserConnection,
} from './helpers';
import { SPEECH_WAV } from './video-call';

export const CALL_SCREEN = '.full-screen-call-view.in-call';
export const JOIN_PREVIEW = '.modal .camera-preview-video';
export const OWN_VIDEO = '.video-panel .video-streaming-preview';
export const REMOTE_VIDEO = '.video-panel .remote-video-container';

export const NARROW = { width: 390, height: 844 };
export const WIDE = { width: 1440, height: 900 };

export interface Users {
    conn: BrowserConnection;
    contexts: BrowserContext[];
    caller: Page;
    callee: Page;
}

export async function newUserPage(
    conn: BrowserConnection,
    email: string,
    viewport: { width: number; height: number },
): Promise<{ context: BrowserContext; page: Page }> {
    const isNarrow = viewport.width < 768;
    // No touch: the ring buttons attach a swipe-to-answer controller on touch devices.
    const context = await conn.browser.newContext({
        ignoreHTTPSErrors: true,
        locale: 'en-US',
        viewport,
        deviceScaleFactor: isNarrow ? 2 : 1,
        isMobile: isNarrow,
        hasTouch: false,
    });
    // The fake-device flags answer getUserMedia, but the app reads the Permissions API first.
    await context.grantPermissions(['microphone', 'camera'], { origin: BASE_URL });
    const page = await context.newPage();
    page.on('pageerror', e => console.log(`PAGEERROR[${email}]:`, e.message));
    await ensureSignedIn(page, email);
    return { context, page };
}

export async function signInBoth(viewport: { width: number; height: number }): Promise<Users> {
    // Real speech, not the fake mic's beep: a silent recording idles out, and the call with it.
    const conn = await connectBrowser({ fakeAudioFile: SPEECH_WAV });
    // Sequential sign-ins: parallel ones race on the shared server flow (see vitest.config.e2e.ts).
    const caller = await newUserPage(conn, TEST_EMAIL, viewport);
    const callee = await newUserPage(conn, TEST_EMAIL_2, viewport);
    for (const page of [caller.page, callee.page]) {
        await skipOnboarding(page);
        await setIncompleteUI(page, true);
    }
    return { conn, contexts: [caller.context, callee.context], caller: caller.page, callee: callee.page };
}

export async function signOutBoth(users: Users | undefined) {
    if (!users)
        return;

    // Unload before closing: a context closed outright leaves its server-side circuit alive for
    // about a minute, still ringing and heartbeating as this account into the next run.
    for (const page of [users.caller, users.callee]) {
        await setIncompleteUI(page, false).catch(() => { /* ignore */ });
        await page.goto('about:blank').catch(() => { /* ignore */ });
    }
    for (const context of users.contexts)
        await context.close().catch(() => { /* ignore */ });
    if (users.conn.ownsBrowser) {
        await users.conn.context.close().catch(() => { /* ignore */ });
        await users.conn.browser.close().catch(() => { /* ignore */ });
    }
}

export async function getUserId(page: Page): Promise<string> {
    return await page.evaluate(() => (window as unknown as { debugUI: { getUserId(): Promise<string> } })
        .debugUI.getUserId());
}

export async function send(page: Page, text: string) {
    // Focused, not clicked: the record button overlaps the editor on the narrow layout.
    const editor = page.locator('#message-input .editor-content[contenteditable="true"]').first();
    await editor.waitFor({ state: 'visible', timeout: 30_000 });
    await editor.focus();
    await page.keyboard.type(text, { delay: 20 });
    await page.keyboard.press('Enter');
    await page.waitForTimeout(1_500);
}

/** Opens the peer chat on both sides and has each post there: the callee's reply opens the peer-call gate. */
export async function openPeerChat(caller: Page, callee: Page) {
    const userIds = [await getUserId(caller), await getUserId(callee)].sort();
    const pm = withUILanguage(`${BASE_URL}/chat/p-${userIds.join('-')}`);
    await caller.goto(pm, { waitUntil: 'domcontentloaded' });
    await send(caller, 'call test');
    await callee.goto(pm, { waitUntil: 'domcontentloaded' });
    await send(callee, 'call test reply');
    await caller.reload({ waitUntil: 'domcontentloaded' });
}

/** Dials from the caller's open peer chat and accepts on the callee. */
export async function dialPeerCall(caller: Page, callee: Page) {
    const callButton = caller.locator('.btn-start-call').first();
    await callButton.waitFor({ state: 'visible', timeout: 30_000 });
    await callButton.click();
    const accept = callee.locator('.btn-call.c-accept').first();
    await accept.waitFor({ state: 'visible', timeout: 30_000 });
    await accept.click();
}

export async function startPeerCall(caller: Page, callee: Page) {
    await openPeerChat(caller, callee);
    await dialPeerCall(caller, callee);
}

/**
 * Ends whatever the page still holds of a call, one control at a time, until the chat is quiet.
 * A call left live keeps the chat busy, and the next test finds no call button there.
 */
export async function hangUpIfAny(page: Page | undefined) {
    if (!page)
        return;

    // Most specific first: the recorder toggle would restart a recording that a hang-up is still stopping.
    const controls = [
        page.locator(`${CALL_SCREEN} .c-call-bar .btn-glass.talking`).first(),
        page.locator('.video-panel .btn-glass.talking').first(),
        page.locator('.chat-audio-controls .c-hangup').first(),
        // On a wide screen an active call has no screen of its own: it ends with the recording
        page.locator('.chat-audio-panel .recorder-wrapper.record-on button').first(),
    ];
    let quietPolls = 0;
    for (let i = 0; i < 24 && quietPolls < 2; i++) {
        let hasClicked = false;
        for (const control of controls) {
            if (!await control.isVisible().catch(() => false))
                continue;

            await control.click({ timeout: 2_000 }).catch(() => { /* retried */ });
            hasClicked = true;
            break;
        }
        const isInCall = hasClicked || await isShown(page, '.collapsed-call-view');
        quietPolls = isInCall ? 0 : quietPolls + 1;
        await page.waitForTimeout(500);
    }
}

/** Expanded, opaque and not hidden: `isVisible` alone is true for a panel still fading in over the chat. */
export function isVideoCoveringScreen(page: Page): Promise<boolean> {
    return page.evaluate(() => {
        const panel = document.querySelector('.video-panel.expanded');
        const content = panel?.querySelector('.video-panel-content');
        if (!panel || !content)
            return false;

        // A panel being closed is hidden before it leaves the DOM
        const panelStyle = getComputedStyle(panel);
        return panelStyle.visibility !== 'hidden' && panelStyle.opacity === '1'
            && getComputedStyle(content).opacity === '1';
    }).catch(() => false);
}

export function isShown(page: Page, selector: string): Promise<boolean> {
    return page.locator(selector).first().isVisible().catch(() => false);
}
