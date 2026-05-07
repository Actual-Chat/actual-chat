/**
 * E2E test: Peer-chat Temporary vs Regular contact state — single-browser flow.
 *
 * Issue #3840: when User A PMs User B with no prior contact relationship,
 * one side ends up with `ContactState.Regular` while the other stays
 * `ContactState.Temporary`. `ChatsBackend.GetPeerChatRules` strips
 * Upload / WriteAudio / WriteVideo / ReadAudio / ReadVideo permissions
 * when `!peerContact.IsStoredContact`, so the asymmetric state shows up
 * as one user having the record button (and the whole ChatAudioPanel)
 * while the other does not. That's the user-facing bug indicator.
 *
 * The "Add to contacts" banner uses a *different* condition — it shows
 * when the local Contact's `Version <= 0` (`HasVersionExt.IsStored()`),
 * i.e. before any contact is persisted to the DB. After the first PM
 * both contacts are persisted, so the banner is **not** the indicator
 * for #3840 — the record button is. We document the banner's absence
 * in the standard peer-chat flow as a separate test, and leave the
 * explicit-add / dismiss flows as `it.todo` — no scenario in the
 * current codebase reliably triggers the banner for peer chats.
 *
 * Why single browser: connecting Playwright over CDP from this Docker
 * container to host Chrome is blocked by Chrome's loopback-only
 * "Browser context management is not supported" rule (host.docker.internal
 * is a non-localhost peer). Sequential sign-out / sign-in on a single
 * browser sidesteps that, so the test is visible whether you run via
 * `c chrome` (one Chrome) on the host or in headless mode.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/peer-contacts.test.ts --config vitest.config.e2e.ts
 *
 * Setup:
 *   - server-loop.cmd / run-watch.cmd running on the host
 *   - `c chrome` for one visible browser via CDP. Headless works too —
 *     set AC_E2E_BROWSER=headless.
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Page } from 'playwright';
import {
    BASE_URL,
    type BrowserConnection,
    addToContactsBanner,
    connectBrowser,
    ensureSignedInAs,
    getUserIdOf,
    newTestEmail,
    peerChatPath,
    recordButton,
    screenshot,
    waitForAppReady,
    waitForChatReady,
} from './helpers';

const shot = (name: string) => screenshot('peer-contacts', name);

async function openPeerChat(page: Page, otherUserId: string): Promise<string> {
    const myId = await getUserIdOf(page);
    const path = peerChatPath(myId, otherUserId);
    await page.goto(`${BASE_URL}${path}`, { waitUntil: 'domcontentloaded', timeout: 60_000 });
    await waitForAppReady(page, 60_000);
    await waitForChatReady(page, 30_000);
    return path;
}

async function sendMessage(page: Page, text: string) {
    const messageInput = page.locator('#message-input .editor-content[contenteditable="true"]').first();
    await messageInput.waitFor({ state: 'visible', timeout: 15_000 });
    await messageInput.click();
    await page.waitForTimeout(150);
    await page.keyboard.type(text);
    const sendButton = page.locator('button.post-message').first();
    if (await sendButton.isVisible({ timeout: 1000 }).catch(() => false)) {
        await sendButton.click();
    } else {
        await page.keyboard.press('Control+Enter');
    }
    await page.locator(`.chat-message-markup:has-text("${text}")`).first()
        .waitFor({ state: 'visible', timeout: 15_000 });
}

interface ChatState {
    bannerVisible: boolean;
    recordButtonVisible: boolean;
    audioPanelCount: number;
}

async function probeChatState(page: Page): Promise<ChatState> {
    return {
        bannerVisible: await addToContactsBanner(page).isVisible({ timeout: 4_000 }).catch(() => false),
        recordButtonVisible: await recordButton(page).isVisible({ timeout: 4_000 }).catch(() => false),
        audioPanelCount: await page.locator('.chat-audio-panel, [class*="ChatAudioPanel"]').count().catch(() => 0),
    };
}

let conn: BrowserConnection;
let page: Page;
let emailA: string;
let emailB: string;
let userIdA: string;
let userIdB: string;

beforeAll(async () => {
    conn = await connectBrowser();
    page = await conn.context.newPage();
    emailA = newTestEmail('a');
    emailB = newTestEmail('b');
    // Register both accounts up front so we can capture their userIds before
    // any chat work. ensureSignedInAs handles the sign-out-then-sign-in
    // dance for switching between accounts on the same page.
    await ensureSignedInAs(page, emailA);
    userIdA = await getUserIdOf(page);
    await ensureSignedInAs(page, emailB);
    userIdB = await getUserIdOf(page);
    if (userIdA.startsWith('~') || userIdB.startsWith('~'))
        throw new Error(`Sign-in failed: A=${userIdA} B=${userIdB}`);
    console.log(`[peer-contacts] A=${userIdA} (${emailA})`);
    console.log(`[peer-contacts] B=${userIdB} (${emailB})`);
}, 240_000);

afterAll(async () => {
    /* eslint-disable @typescript-eslint/no-unnecessary-condition -- page may be unset if beforeAll fails */
    if (page) await page.close().catch(() => { /* ignore */ });
    if (conn?.ownsBrowser) {
        await conn.context.close().catch(() => { /* ignore */ });
        await conn.browser.close().catch(() => { /* ignore */ });
    }
    /* eslint-enable @typescript-eslint/no-unnecessary-condition */
});

describe('peer chat #3840: single-browser sign-in switching', () => {
    // Tests run sequentially on the same page, switching identity between
    // them. Persistent server-side state (the peer chat, the contact records,
    // messages) is the same regardless of which user is currently signed in,
    // so we can probe each side after the relevant action and compare.

    let aAfterSend: ChatState;
    let bAfterReceive: ChatState;

    it.fails(
        'case 1 (BUG #3840): after A → B PM, A and B should have matching record-button availability',
        async () => {
            // A sends.
            await ensureSignedInAs(page, emailA);
            await openPeerChat(page, userIdB);
            await sendMessage(page, `Hello from A at ${new Date().toISOString()}`);
            await page.waitForTimeout(2_000);
            await page.screenshot({ path: shot('01-A-after-send') });
            aAfterSend = await probeChatState(page);
            console.log(`[peer-contacts] case 1: A=${JSON.stringify(aAfterSend)}`);

            // B opens the same chat.
            await ensureSignedInAs(page, emailB);
            await openPeerChat(page, userIdA);
            await page.locator('.chat-message-markup').first()
                .waitFor({ state: 'visible', timeout: 20_000 });
            await page.waitForTimeout(2_000);
            await page.screenshot({ path: shot('01-B-after-receive') });
            bAfterReceive = await probeChatState(page);
            console.log(`[peer-contacts] case 1: B=${JSON.stringify(bAfterReceive)}`);

            // EXPECTED (correct behaviour): both users in the same exchange
            // should land in the same contact state. Currently they don't —
            // typically B (recipient) gets Regular and A (sender) stays
            // Temporary, so A.rec=false while B.rec=true.
            //
            // `it.fails` passes while the bug is present and starts FAILING
            // (unexpectedly passing) once #3840 is fixed — promote it back
            // to a regular `it()` then.
            expect(
                aAfterSend.recordButtonVisible,
                `Asymmetric contact state (#3840): A.rec=${aAfterSend.recordButtonVisible} ` +
                `but B.rec=${bAfterReceive.recordButtonVisible}.`,
            ).toBe(bAfterReceive.recordButtonVisible);
        },
        240_000,
    );

    it('case 2: "Add to contacts" banner does NOT appear in the standard peer-chat flow', async () => {
        // The banner shows iff the local Contact's `Version <= 0` (i.e. not
        // persisted). After the first PM the server persists both sides'
        // Contacts immediately, so neither user sees the banner — this is
        // what AddToContactsBanner.razor:52 evaluates to false.
        // We're already signed in as B from case 1.
        const b = await probeChatState(page);
        await ensureSignedInAs(page, emailA);
        await openPeerChat(page, userIdB);
        await page.waitForTimeout(2_000);
        const a = await probeChatState(page);
        console.log(`[peer-contacts] case 2: A.banner=${a.bannerVisible} B.banner=${b.bannerVisible}`);
        expect(a.bannerVisible, 'A should not see banner — Contact persisted by send').toBe(false);
        expect(b.bannerVisible, 'B should not see banner — Contact persisted by receive').toBe(false);
    }, 120_000);

    it.todo('case 3: explicit add via banner click');
    // The banner does not appear in the standard peer-chat flow (see case 2),
    // so there is no way to click it in this scenario. If a future change
    // makes the banner appear (e.g. lazy contact creation), wire up the
    // click + `expect(recordButtonVisible).toBe(true)` assertion here.

    it.todo('case 4: edge case — A adds explicitly, B dismisses banner');
    // Same blocker as case 3 — depends on the banner being reachable.

    it('B reply: B record button stays available, and afterwards A also gains it (auto-promote)', async () => {
        // Switch to B and reply.
        await ensureSignedInAs(page, emailB);
        await openPeerChat(page, userIdA);
        await page.locator('.chat-message-markup').first()
            .waitFor({ state: 'visible', timeout: 20_000 });
        await sendMessage(page, `Reply from B at ${new Date().toISOString()}`);
        await page.waitForTimeout(2_500);
        await page.screenshot({ path: shot('02-B-after-reply') });
        const bAfterReply = await probeChatState(page);
        console.log(`[peer-contacts] after B.reply: B=${JSON.stringify(bAfterReply)}`);
        expect(bAfterReply.recordButtonVisible, 'B record button should be visible after replying').toBe(true);

        // Switch to A and verify auto-promote. The promotion runs server-side
        // and propagates back via Fusion ComputedState — re-poll because the
        // page was on B's view a moment ago.
        await ensureSignedInAs(page, emailA);
        await openPeerChat(page, userIdB);
        let a = await probeChatState(page);
        for (let i = 0; i < 20 && !a.recordButtonVisible; i++) {
            await page.waitForTimeout(1_000);
            a = await probeChatState(page);
        }
        await page.screenshot({ path: shot('03-A-after-B-reply') });
        console.log(`[peer-contacts] after B.reply: A=${JSON.stringify(a)}`);
        expect(a.recordButtonVisible, 'A record button should appear after B replied (auto-promote)').toBe(true);
    }, 240_000);
});
