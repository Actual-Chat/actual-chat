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

export async function dismissCookieConsent(page: Page) {
    const btn = page.locator('button:has-text("Accept all cookies")');
    if (await btn.isVisible({ timeout: 3000 }).catch(() => false)) {
        await btn.click();
        await page.waitForTimeout(500);
    }
}

export async function skipOnboarding(page: Page) {
    await page.evaluate(() => {
        /* eslint-disable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-assignment, @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-unsafe-call, @typescript-eslint/no-unsafe-return */
        const debugUI = (window as any).debugUI;
        if (debugUI) {
            debugUI.resetOnboarding(false);
            debugUI.resetBubbles(false);
        }
        /* eslint-enable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-assignment, @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-unsafe-call, @typescript-eslint/no-unsafe-return */
    });
    // debugUI.resetOnboarding/resetBubbles are fire-and-forget on the JS side — the backend
    // call settles asynchronously, so the OnboardingModal (full-screen stepper) and bubble
    // tooltips may still render momentarily. Dismiss anything that appears.
    for (let i = 0; i < 20; i++) {
        // OnboardingModal — rendered as `<div class=" modal-overlay" id="Modal-OnboardingModal...">`
        // and intercepts pointer events on everything behind it.
        const onboardingModal = page.locator('[id^="Modal-OnboardingModal"]').first();
        if (await onboardingModal.isVisible().catch(() => false)) {
            const skip = onboardingModal.locator('button:has-text("Skip")').first();
            const close = onboardingModal.locator('.icon-close').first();
            if (await skip.isVisible().catch(() => false))
                await skip.click().catch(() => { /* ignore */ });
            else if (await close.isVisible().catch(() => false))
                await close.click().catch(() => { /* ignore */ });
            else
                await page.keyboard.press('Escape').catch(() => { /* ignore */ });
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
    await signInButton.click();
    await page.waitForTimeout(1000);

    // Enter email — TextBox attaches an 800ms-debounced `input` listener via JSInterop
    // in OnAfterRenderAsync. On cold first render the listener can attach *after* fill()
    // dispatches its events, so the Blazor model never updates and submit stays disabled.
    // pressSequentially simulates real typing: input events fire over ~1.4s, guaranteeing
    // some arrive after the listener is attached.
    const emailInput = page.locator('input[type="email"], input[placeholder*="email" i]').first();
    // Cold start in CI can delay the sign-in modal's first render well past 5s.
    await emailInput.waitFor({ timeout: 20000 });
    await emailInput.click();
    await emailInput.pressSequentially(TEST_EMAIL, { delay: 50 });
    // Commit via Tab so Blazor's @onchange binder fires (change event on blur).
    await emailInput.press('Tab');

    // Submit — wait for the button to actually become enabled (past debounce + re-render).
    await page.locator('button[type="submit"]:not([disabled])').first().click({ timeout: 15_000 });
    await page.waitForTimeout(2000);

    // Handle "Account not found" → toggle "Register a new account" and resubmit
    const accountError = page.locator('.c-account-error:has-text("Account not found")');
    if (await accountError.isVisible({ timeout: 2000 }).catch(() => false)) {
        await page.locator('label:has-text("Register a new account")').click();
        await page.waitForTimeout(300);
        await page.locator('button[type="submit"]').first().click();
        await page.waitForTimeout(2000);
    }

    // Enter OTP — wait for any OTP input to render before deciding which layout to use.
    // Registration on a cold CI server can take 20+ seconds before the TOTP step renders.
    await page.locator('input[maxlength="1"], input[inputmode="numeric"], input[type="tel"]')
        .first().waitFor({ timeout: 30000 });
    const digitInputs = page.locator('input[maxlength="1"]');
    const digitCount = await digitInputs.count();
    if (digitCount >= 6) {
        for (let i = 0; i < 6; i++) {
            await digitInputs.nth(i).fill(TEST_OTP[i]);
            await page.waitForTimeout(50);
        }
    } else {
        const otpInput = page.locator('input[inputmode="numeric"], input[type="tel"]').first();
        await otpInput.fill(TEST_OTP);
    }
    await page.waitForTimeout(500);

    // Click verify if visible
    const verifyButton = page.locator('button[type="submit"]').first();
    if (await verifyButton.isVisible({ timeout: 2000 }).catch(() => false)) {
        await verifyButton.click();
        await page.waitForTimeout(3000);
    }
}

/**
 * Navigate to BASE_URL, dismiss cookie consent, sign in if needed, skip onboarding.
 * Returns when the app is ready for interaction.
 */
export async function ensureSignedIn(page: Page) {
    await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(2000);
    await dismissCookieConsent(page);
    if (!await isSignedIn(page))
        await signIn(page);
    await skipOnboarding(page);
}
