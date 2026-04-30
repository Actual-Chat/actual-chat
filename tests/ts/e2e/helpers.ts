/**
 * Shared helpers for E2E tests.
 *
 * - Browser connection: respects AC_E2E_BROWSER env var (auto/cdp/headless)
 * - Config: reads BASE_URL from .env (HostSettings__BaseUri)
 * - Page helpers: sign-in, cookie consent, onboarding, screenshots
 */

import { chromium, type Browser, type BrowserContext, type Page } from 'playwright';
import * as fs from 'fs';
import * as path from 'path';
import { execSync } from 'child_process';
// --- Configuration ---

export const TEST_EMAIL = 'test-claude-agent@actual.chat';
export const TEST_OTP = '111111';

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

// --- Browser connection ---

export interface BrowserConnection {
    browser: Browser;
    context: BrowserContext;
    /** true when we launched headless Chromium (caller should close browser) */
    ownsBrowser: boolean;
}

/**
 * Connect to a browser based on AC_E2E_BROWSER env var:
 * - "auto" (default): try CDP, fall back to headless
 * - "cdp": force CDP — fail if Chrome isn't running
 * - "headless": force headless Chromium
 */
export async function connectBrowser(): Promise<BrowserConnection> {
    const mode = (process.env.AC_E2E_BROWSER ?? 'auto').toLowerCase();

    if (mode === 'headless')
        return launchHeadless();

    if (mode === 'cdp') {
        const conn = await tryCdp();
        if (!conn) throw new Error('AC_E2E_BROWSER=cdp but Chrome CDP is not reachable');
        return conn;
    }

    // auto: try CDP, fall back to headless
    const conn = await tryCdp();
    if (conn) return conn;
    console.log('CDP not available, falling back to headless Chromium');
    return launchHeadless();
}

async function tryCdp(): Promise<BrowserConnection | null> {
    const hosts = getCdpHosts();
    for (const host of hosts) {
        const endpoint = `http://${host}:9222`;
        try {
            const browser = await chromium.connectOverCDP(endpoint, { timeout: 3000 });
            const contexts = browser.contexts();
            const context = contexts.length > 0 ? contexts[0] : await browser.newContext();
            console.log(`Connected to Chrome via CDP at ${endpoint}`);
            return { browser, context, ownsBrowser: false };
        } catch {
            // try next host
        }
    }
    return null;
}

/**
 * Hosts to try when reaching a service on the host machine.
 * On macOS Docker with --network host, `localhost` may resolve to ::1 while the
 * host service binds IPv4 only — fall back to host.docker.internal's resolved IP.
 */
export function getLocalHosts(): string[] {
    const hosts = ['localhost'];
    if (process.env.AC_OS === 'Linux in Docker') {
        try {
            const ip = execSync("getent ahosts host.docker.internal 2>/dev/null | awk 'NR==1{print $1}'", {
                encoding: 'utf-8',
                timeout: 2000,
            }).trim();
            if (ip && ip !== 'localhost') hosts.push(ip);
        } catch {
            // not in Docker or getent not available
        }
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

// --- Page helpers ---

/**
 * Wait until Blazor has hydrated and the app body has rendered.
 *
 * The Blazor host page shows a full-viewport `#web-splash` overlay while the
 * WASM/server bundles load. `page.goto(..., { waitUntil: 'domcontentloaded' })`
 * returns long before that — a fixed `waitForTimeout(3000)` is a race that CI
 * loses (cold .NET start, fresh DB, network variance). Instead, wait until at
 * least one app-level landmark is visible — any of these indicates that the
 * Blazor app has hydrated past the splash.
 */
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

/**
 * Wait until a chat page has settled into one of its terminal states:
 * an editable message input, a Join button, or a sign-in prompt.
 */
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
    // The consent banner can render slightly after the splash clears — wait for
    // either button to show up, but don't fail if it never does (some entry points
    // skip the banner when cookies are already accepted via localStorage).
    // "Accept all cookies" and "Necessary cookies only" both dismiss it.
    const btn = page.locator(
        'button:has-text("Accept all cookies"), button:has-text("Necessary cookies only")',
    ).first();
    try {
        await btn.waitFor({ state: 'visible', timeout });
    } catch {
        return; // banner never appeared — nothing to dismiss
    }
    await btn.click();
    // Wait for the banner to disappear before returning, so the sign-in button
    // underneath is actually clickable without an intercepting overlay.
    await page.locator('.cookie-settings').waitFor({ state: 'hidden', timeout: 5_000 })
        .catch(() => { /* some pages don't use .cookie-settings wrapper */ });
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
        // Force-hide any onboarding modal that's already mounted. resetOnboarding
        // is a backend-only state change — it doesn't forcibly close the currently
        // displayed OnboardingModal, whose <Stepper> keeps rendering its current
        // step until the user clicks next. In headless CI the stepper-footer
        // intercepts pointer events and we can't always find a button that
        // actually advances past it. Hiding the overlay lets us reach the UI
        // underneath; the modal will re-hide itself on next state observation.
        document.querySelectorAll('[id^="Modal-OnboardingModal"]').forEach(el => {
            (el as HTMLElement).style.display = 'none';
            (el as HTMLElement).style.pointerEvents = 'none';
        });
        /* eslint-enable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-assignment, @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-unsafe-call */
    });
    // debugUI.resetOnboarding/resetBubbles are fire-and-forget on the JS side — the backend
    // call settles asynchronously, so the OnboardingModal (full-screen stepper) and bubble
    // tooltips may still render momentarily. Dismiss anything that appears.
    //
    // The footer button text varies per Stepper step: "Skip"/"Decline" for skip, "Next" /
    // "Enable Telemetry" / "Start messaging" / custom NextTitle for next. Match them all.
    // The close (X) button is only shown when CanBeClosed=true, so we can't rely on it.
    for (let i = 0; i < 20; i++) {
        // OnboardingModal — rendered as `<div class=" modal-overlay" id="Modal-OnboardingModal...">`
        // and intercepts pointer events on everything behind it.
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

        // Bubble tooltip
        const bubbleBtn = page.locator('.bubble-buttons button:has-text("Skip"), .bubble-buttons button:has-text("Ok")').first();
        if (await bubbleBtn.isVisible().catch(() => false)) {
            await bubbleBtn.click().catch(() => { /* ignore */ });
            await page.waitForTimeout(300);
            continue;
        }

        break; // nothing left to dismiss
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
    // force: true — a stale OnboardingModal overlay can still intercept pointer
    // events on a freshly-created context if the previous test's DOM leaked in
    // via a dev-server re-render; the sign-in button itself is always safe to click.
    await signInButton.click({ force: true });
    await page.waitForTimeout(1000);

    // Enter email — TextBox attaches an 800ms-debounced `input` listener via JSInterop
    // in OnAfterRenderAsync. On cold first render the listener can attach *after* fill()
    // dispatches its events, so the Blazor model never updates and submit stays disabled.
    // pressSequentially simulates real typing: input events fire over ~1.4s, guaranteeing
    // some arrive after the listener is attached.
    const emailInput = page.locator('input[type="email"], input[placeholder*="email" i]').first();
    // Cold start in CI can delay the sign-in modal's first render well past 5s.
    try {
        await emailInput.waitFor({ timeout: 20000 });
    } catch (e) {
        await page.screenshot({ path: screenshot('e2e', 'signin-failed') }).catch(() => { /* ignore */ });
        throw e;
    }
    await emailInput.click();
    await emailInput.pressSequentially(TEST_EMAIL, { delay: 50 });
    // Commit via Tab so Blazor's @onchange binder fires (change event on blur).
    await emailInput.press('Tab');

    // Submit — wait for the button to actually become enabled (past debounce + re-render).
    await page.locator('button[type="submit"]:not([disabled])').first().click({ timeout: 15_000 });

    // Wait for the TOTP step to render. TotpInput renders 6 inputs with inputmode="numeric"
    // and auto-verifies on completion (no separate "Verify" button — see TotpInput.razor).
    // Cold start in CI can delay the TOTP step well past 10s.
    const otpDigits = page.locator('.totp-input input[inputmode="numeric"]');
    await otpDigits.first().waitFor({ state: 'visible', timeout: 30_000 });

    // Fill all 6 digits — fill() bypasses pointer events, so it works even if the
    // "Register new account?" ConfirmModal has rendered on top of the TOTP step.
    for (let i = 0; i < 6; i++) {
        await otpDigits.nth(i).fill(TEST_OTP[i]);
        await page.waitForTimeout(50);
    }

    // Handle "Register new account?" ConfirmModal that appears for unknown emails.
    // For new accounts, AccountUI.MonitorPendingRegistration shows a ConfirmModal
    // (title "Register new account?", confirm button "Register"). The TOTP completion
    // triggers the registration prompt; we must click "Register" to actually create the account.
    // For existing accounts, no modal appears and sign-in completes after TOTP verification.
    const registerModal = page.locator('[id^="Modal-ConfirmModal"]:has-text("Register new account")').first();
    const signedInLandmark = page.locator('.chat-list, .account-dropdown').first();
    await Promise.race([
        registerModal.waitFor({ state: 'visible', timeout: 30_000 })
            .then(async () => {
                await registerModal.locator('button:has-text("Register")').click({ timeout: 5_000 });
                await registerModal.waitFor({ state: 'hidden', timeout: 10_000 }).catch(() => { /* ignore */ });
            }),
        signedInLandmark.waitFor({ state: 'visible', timeout: 30_000 }),
    ]).catch(() => { /* one of them timed out — let the caller verify state */ });
}

/**
 * Navigate to BASE_URL, dismiss cookie consent, sign in if needed, skip onboarding.
 * Returns when the app is ready for interaction.
 */
export async function ensureSignedIn(page: Page) {
    await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
    await waitForAppReady(page);
    await dismissCookieConsent(page);
    if (!await isSignedIn(page))
        await signIn(page);
    await skipOnboarding(page);
}
