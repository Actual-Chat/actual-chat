import { chromium, type Browser, type BrowserContext, type Page } from 'playwright';
import * as fs from 'fs';
import * as path from 'path';
import { execSync } from 'child_process';

export const TEST_EMAIL = 'test-claude-agent@actual.chat';
export const TEST_OTP = '111111';

export const TEST_EMAIL_DOMAIN = '@actual.chat';
export const TEST_EMAIL_PREFIX = 'test-claude-';

// Generates a random `test-claude-<tag>-<rand>@actual.chat` email — the server
// dev-bypass accepts ANY `test-*@actual.chat` with TOTP 111111 on local hosts,
// so a fresh suffix per test = fresh account state.
export function newTestEmail(tag = 'u'): string {
    const rand = Math.random().toString(36).slice(2, 10) + Date.now().toString(36).slice(-4);
    return `${TEST_EMAIL_PREFIX}${tag}-${rand}${TEST_EMAIL_DOMAIN}`;
}

export function loadBaseUrl(): string {
    const fromEnv = process.env.HostSettings__BaseUri;
    if (fromEnv) return fromEnv;
    const envPath = path.resolve(process.cwd(), '.env');
    if (fs.existsSync(envPath)) {
        const match = /^HostSettings__BaseUri=(.+)$/m.exec(fs.readFileSync(envPath, 'utf-8'));
        if (match) return match[1].trim();
    }
    return 'https://local.voxt.ai';
}

export const BASE_URL = loadBaseUrl();

const tmpDir = path.join(process.cwd(), 'tmp');
if (!fs.existsSync(tmpDir)) {
    fs.mkdirSync(tmpDir, { recursive: true });
}

export function screenshot(prefix: string, name: string): string {
    return path.join(tmpDir, `${prefix}-${name}.png`);
}

export interface BrowserConnection {
    browser: Browser;
    context: BrowserContext;
    ownsBrowser: boolean;
}

/** AC_E2E_BROWSER: "auto" (CDP, fallback headless), "cdp", or "headless". */
export async function connectBrowser(): Promise<BrowserConnection> {
    const mode = (process.env.AC_E2E_BROWSER ?? 'auto').toLowerCase();

    if (mode === 'headless')
        return launchHeadless();

    if (mode === 'cdp') {
        const conn = await tryCdp();
        if (!conn) throw new Error('AC_E2E_BROWSER=cdp but Chrome CDP is not reachable');
        return conn;
    }

    const conn = await tryCdp();
    if (conn) return conn;
    console.log('CDP not available, falling back to headless Chromium');
    return launchHeadless();
}

async function tryCdp(): Promise<BrowserConnection | null> {
    for (const host of getCdpHosts()) {
        const endpoint = `http://${host}:9222`;
        try {
            const browser = await chromium.connectOverCDP(endpoint, { timeout: 3000 });
            const contexts = browser.contexts();
            const context = contexts.length > 0 ? contexts[0] : await browser.newContext();
            console.log(`Connected to Chrome via CDP at ${endpoint}`);
            return { browser, context, ownsBrowser: false };
        } catch { /* try next host */ }
    }
    return null;
}

/** On macOS Docker with --network host, `localhost` may resolve to ::1 while the
 *  host binds IPv4 only — fall back to host.docker.internal's resolved IP. */
export function getLocalHosts(): string[] {
    const hosts = ['localhost'];
    if (process.env.AC_OS === 'Linux in Docker') {
        try {
            const ip = execSync("getent ahosts host.docker.internal 2>/dev/null | awk 'NR==1{print $1}'", {
                encoding: 'utf-8',
                timeout: 2000,
            }).trim();
            if (ip && ip !== 'localhost') hosts.push(ip);
        } catch { /* not in Docker */ }
    }
    return hosts;
}

function getCdpHosts(): string[] {
    return getLocalHosts();
}

async function launchHeadless(): Promise<BrowserConnection> {
    try {
        const browser = await chromium.launch({
            headless: true,
            args: ['--no-sandbox', '--disable-setuid-sandbox'],
        });
        const context = await browser.newContext({ ignoreHTTPSErrors: true });
        console.log('Launched headless Chromium');
        return { browser, context, ownsBrowser: true };
    } catch (e: unknown) {
        if (e instanceof Error
            && (e.message.includes('Executable doesn\'t exist') || e.message.includes('browserType.launch'))) {
            throw new Error(
                'Chromium is not installed. Run: npm run test:e2e:install'
            );
        }
        throw e;
    }
}

/** Wait past the #web-splash overlay until any Blazor landmark is visible. */
export async function waitForAppReady(page: Page, timeout = 30_000) {
    await page.locator([
        '.chat-list',
        '.account-dropdown',
        'button.signin-button-group',
        'button.signin-button',
        '.chat-view',
        '.chat-message-editor',
        '.settings-modal',
        '.join-footer',
        '.signin-footer',
    ].join(', ')).first().waitFor({ state: 'visible', timeout });
}

/** Wait for a chat page to settle: editable input, Join button, or sign-in prompt. */
export async function waitForChatReady(page: Page, timeout = 20_000) {
    await page.locator([
        '#message-input .editor-content[contenteditable="true"]',
        'button:has-text("Join this chat")',
        'button:has-text("Join anonymously")',
        '.signin-footer button.signin-button',
        '.signin-footer button',
    ].join(', ')).first().waitFor({ state: 'visible', timeout });
}

export async function dismissCookieConsent(page: Page, timeout = 10_000) {
    const btn = page.locator(
        'button:has-text("Accept all cookies"), button:has-text("Necessary cookies only")',
    ).first();
    try {
        await btn.waitFor({ state: 'visible', timeout });
    } catch {
        return;
    }
    await btn.click();
    // Wait for the banner to disappear before returning so it doesn't intercept
    // the next click (cookies-accepted entry points have no .cookie-settings wrapper).
    await page.locator('.cookie-settings').waitFor({ state: 'hidden', timeout: 5_000 })
        .catch(() => { /* ignore */ });
    await page.waitForTimeout(200);
}

export async function skipOnboarding(page: Page) {
    await page.evaluate(() => {
        /* eslint-disable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-assignment, @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-unsafe-call */
        const debugUI = (window as any).debugUI;
        if (debugUI) {
            debugUI.resetOnboarding(false);
            debugUI.resetBubbles(false);
        }
        // resetOnboarding is a backend-only state change — it doesn't unmount the
        // currently rendered OnboardingModal, whose stepper-footer keeps intercepting
        // pointer events. Hide the overlay so the UI underneath becomes clickable.
        document.querySelectorAll('[id^="Modal-OnboardingModal"]').forEach(el => {
            (el as HTMLElement).style.display = 'none';
            (el as HTMLElement).style.pointerEvents = 'none';
        });
        /* eslint-enable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-assignment, @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-unsafe-call */
    });
    // resetOnboarding/resetBubbles are fire-and-forget — the modal/bubbles may
    // still render briefly. The footer button text varies per step (Skip/Decline/
    // Next/Start messaging/...), so match them all.
    for (let i = 0; i < 20; i++) {
        const onboardingModal = page.locator('[id^="Modal-OnboardingModal"]').first();
        if (await onboardingModal.isVisible().catch(() => false)) {
            const footerBtn = onboardingModal.locator(
                '.onboarding-footer button:has-text("Start messaging"), ' +
                '.onboarding-footer button:has-text("Skip"), ' +
                '.onboarding-footer button:has-text("Decline"), ' +
                '.onboarding-footer button:has-text("Next"), ' +
                '.onboarding-footer .btn-cancel, ' +
                '.onboarding-footer .btn-primary',
            ).first();
            const headerClose = onboardingModal.locator('header .icon-close, .dialog-header .icon-close').first();
            if (await footerBtn.isVisible().catch(() => false)) {
                await footerBtn.click({ force: true, timeout: 2_000 }).catch(() => { /* ignore */ });
            } else if (await headerClose.isVisible().catch(() => false)) {
                await headerClose.click({ force: true, timeout: 2_000 }).catch(() => { /* ignore */ });
            } else {
                await page.keyboard.press('Escape').catch(() => { /* ignore */ });
            }
            await page.waitForTimeout(400);
            continue;
        }

        const bubbleBtn = page.locator('.bubble-buttons button:has-text("Skip"), .bubble-buttons button:has-text("Ok")').first();
        if (await bubbleBtn.isVisible().catch(() => false)) {
            await bubbleBtn.click().catch(() => { /* ignore */ });
            await page.waitForTimeout(300);
            continue;
        }

        break;
    }
}

export async function isSignedIn(page: Page): Promise<boolean> {
    const signedIn = page.locator('.chat-list, .account-dropdown').first();
    const notSignedIn = page.locator('button.signin-button-group, button.signin-button').first();
    return Promise.race([
        signedIn.waitFor({ state: 'visible', timeout: 15000 }).then(() => true),
        notSignedIn.waitFor({ state: 'visible', timeout: 15000 }).then(() => false),
    ]);
}

export async function signIn(page: Page) {
    const signInButton = page.locator('button.signin-button-group, button.signin-button').first();
    await signInButton.waitFor({ state: 'visible', timeout: 10000 });
    // force: a stale OnboardingModal overlay can still intercept clicks on a fresh context.
    await signInButton.click({ force: true });
    await page.waitForTimeout(1000);

    const emailInput = page.locator('input[type="email"], input[placeholder*="email" i]').first();
    try {
        await emailInput.waitFor({ timeout: 20000 });
    } catch (e) {
        await page.screenshot({ path: screenshot('e2e', 'signin-failed') }).catch(() => { /* ignore */ });
        throw e;
    }
    await emailInput.click();
    // pressSequentially: TextBox attaches its 800ms-debounced input listener in
    // OnAfterRenderAsync, so a single fill() can land before the listener exists.
    await emailInput.pressSequentially(TEST_EMAIL, { delay: 50 });
    // Tab → fires Blazor's @onchange on blur.
    await emailInput.press('Tab');

    await page.locator('button[type="submit"]:not([disabled])').first().click({ timeout: 15_000 });

    // TotpInput auto-verifies when all 6 digits are entered — no separate verify button.
    const otpDigits = page.locator('.totp-input input[inputmode="numeric"]');
    await otpDigits.first().waitFor({ state: 'visible', timeout: 30_000 });

    // fill() bypasses pointer events, so digits land even if the "Register new
    // account?" ConfirmModal has already rendered on top of the TOTP step.
    for (let i = 0; i < 6; i++) {
        await otpDigits.nth(i).fill(TEST_OTP[i]);
        await page.waitForTimeout(50);
    }

    // Unknown emails → AccountUI.MonitorPendingRegistration shows a ConfirmModal
    // ("Register new account?"); confirm to create the account. Existing accounts
    // skip the modal and sign-in completes directly.
    const registerModal = page.locator('[id^="Modal-ConfirmModal"]:has-text("Register new account")').first();
    const signedInLandmark = page.locator('.chat-list, .account-dropdown').first();
    await Promise.race([
        registerModal.waitFor({ state: 'visible', timeout: 30_000 })
            .then(async () => {
                await registerModal.locator('button:has-text("Register")').click({ timeout: 5_000 });
                await registerModal.waitFor({ state: 'hidden', timeout: 10_000 }).catch(() => { /* ignore */ });
            }),
        signedInLandmark.waitFor({ state: 'visible', timeout: 30_000 }),
    ]).catch(() => { /* caller verifies */ });
}

export async function ensureSignedIn(page: Page) {
    await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
    await waitForAppReady(page);
    await dismissCookieConsent(page);
    if (!await isSignedIn(page))
        await signIn(page);
    await skipOnboarding(page);
}

// === Multi-user (two-browser) support =====================================

export interface TwoBrowserConnection {
    a: BrowserConnection;
    b: BrowserConnection;
}

// Connects two independent browsers. Tries CDP on 9222 + 9223 first (the user
// runs `c chrome*2` to see both); falls back to launching headless Chromium
// for each missing port. The two browsers MUST be separate processes —
// `connectOverCDP(...).newContext()` shares cookies with the default profile.
export async function connectTwoBrowsers(): Promise<TwoBrowserConnection> {
    const mode = (process.env.AC_E2E_BROWSER ?? 'auto').toLowerCase();
    if (mode === 'headless') {
        return {
            a: await launchHeadless(),
            b: await launchHeadless(),
        };
    }

    const portA = await tryCdpAt(9222);
    const portB = await tryCdpAt(9223);
    if (mode === 'cdp' && (!portA || !portB))
        throw new Error('AC_E2E_BROWSER=cdp but Chrome CDP is not reachable on both 9222 and 9223 (run `c chrome*2`)');

    if (!portA && !portB) {
        console.log('CDP not available, launching two headless Chromium browsers');
        return { a: await launchHeadless(), b: await launchHeadless() };
    }
    if (!portB) {
        console.log('CDP only reachable on 9222 — run `c chrome*2` to see both. User B will be headless.');
        return { a: portA!, b: await launchHeadless() };
    }
    if (!portA) {
        console.log('CDP only reachable on 9223 — run `c chrome*2` to see both. User A will be headless.');
        return { a: await launchHeadless(), b: portB };
    }
    return { a: portA, b: portB };
}

async function tryCdpAt(port: number): Promise<BrowserConnection | null> {
    for (const host of getCdpHosts()) {
        const endpoint = `http://${host}:${port}`;
        try {
            const browser = await chromium.connectOverCDP(endpoint, { timeout: 3000 });
            const contexts = browser.contexts();
            const context = contexts.length > 0 ? contexts[0] : await browser.newContext();
            console.log(`Connected to Chrome via CDP at ${endpoint}`);
            return { browser, context, ownsBrowser: false };
        } catch { /* try next host */ }
    }
    return null;
}

export async function disposeBrowserConnection(conn: BrowserConnection) {
    if (conn.ownsBrowser) {
        await conn.context.close().catch(() => { /* ignore */ });
        await conn.browser.close().catch(() => { /* ignore */ });
    }
}

// === debugUI helpers =======================================================

// Whether `window.debugUI` is present on this page. After cold reload it can
// take a beat to attach — callers should reload-and-wait before asserting.
async function hasDebugUI(page: Page): Promise<boolean> {
    return page.evaluate(() => typeof (window as unknown as { debugUI?: unknown }).debugUI !== 'undefined');
}

async function waitForDebugUI(page: Page, timeoutMs = 20_000): Promise<void> {
    const start = Date.now();
    while (Date.now() - start < timeoutMs) {
        if (await hasDebugUI(page)) return;
        await page.waitForTimeout(200);
    }
    throw new Error('debugUI was not attached to window within timeout — is this a local-dev server?');
}

// Reads the signed-in user's id (or guest id, prefixed with `~`).
export async function getUserIdOf(page: Page): Promise<string> {
    await waitForDebugUI(page);
    return page.evaluate(async () => {
        /* eslint-disable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-assignment, @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-unsafe-call */
        const debugUI = (window as any).debugUI;
        const id: string = await debugUI.getUserId();
        return id;
        /* eslint-enable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-assignment, @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-unsafe-call */
    });
}

// Signs the page in as `email` via `debugUI.signIn` (local-dev shortcut).
// Switches identity by clearing cookies then reloading from BASE_URL, which
// is more reliable than `debugUI.signOut()` — that path repeatedly tore
// down Blazor circuits in ways that left the next navigation hanging on
// waitForAppReady. Cookies-only approach is also faster (no server-side
// SignOut command).
export async function ensureSignedInAs(page: Page, email: string): Promise<void> {
    await page.context().clearCookies();
    await page.goto(BASE_URL, { waitUntil: 'domcontentloaded', timeout: 60_000 });
    await waitForAppReady(page, 60_000);
    await dismissCookieConsent(page);
    await waitForDebugUI(page);

    // skipBubbles: false avoids the BubbleHost JS-ref race documented at
    // DebugUI.Auth.cs:88. We use the DOM-based skipOnboarding for both
    // onboarding modals and feature-tip bubbles.
    await page.evaluate(async (email1: string) => {
        /* eslint-disable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-unsafe-call */
        await (window as any).debugUI.signIn(email1, { skipBubbles: false, skipOnboarding: true });
        /* eslint-enable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-unsafe-call */
    }, email);

    await waitForAppReady(page);
    await skipOnboarding(page);
}

// === Peer chat helpers =====================================================

// Builds the peer-chat URL path for a pair of user ids. PeerChatId sorts the
// two ids ordinally — feeding them in the wrong order yields "Chat not found".
export function peerChatPath(userIdA: string, userIdB: string): string {
    const [first, second] = [userIdA, userIdB].sort();
    return `/chat/p-${first}-${second}`;
}

// === Banner / record-button locators =======================================

// "This user is not in your contact list" — shown when the local user's
// contact for the peer is in the Temporary state.
export function addToContactsBanner(page: Page) {
    return page.locator('.banner.banner-warning:has-text("This user is not in your contact list")').first();
}

export function addToContactsButton(page: Page) {
    return addToContactsBanner(page).locator('button:has-text("Add to contacts")').first();
}

export function dismissBannerButton(page: Page) {
    return addToContactsBanner(page).locator('.close-banner').first();
}

// The big record-toggle button. Hidden entirely (whole ChatAudioPanel is
// not rendered) when none of CanWriteAudio/CanReadAudio/CanWriteVideo/
// CanReadVideo holds — which is the case for Temporary peer contacts.
export function recordButton(page: Page) {
    return page.locator('.rec-btn').first();
}
