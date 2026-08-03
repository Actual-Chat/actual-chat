import { chromium, type Browser, type BrowserContext, type Page } from 'playwright';
import * as fs from 'fs';
import * as path from 'path';
import { execSync } from 'child_process';

export const TEST_EMAIL = 'test-claude-agent@actual.chat';
export const TEST_EMAIL_2 = 'test-claude-agent-2@actual.chat';
export const TEST_OTP = '111111';

export function loadBaseUrl(): string {
    const fromEnv = process.env.HostSettings__BaseUri;
    if (fromEnv)
        return fromEnv;

    const envPath = path.resolve(process.cwd(), '.env');
    if (fs.existsSync(envPath)) {
        const match = /^HostSettings__BaseUri=(.+)$/m.exec(fs.readFileSync(envPath, 'utf-8'));
        if (match)
            return match[1].trim();
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
        if (!conn)
            throw new Error('AC_E2E_BROWSER=cdp but Chrome CDP is not reachable');

        return conn;
    }

    const conn = await tryCdp();
    if (conn)
        return conn;

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
            args: [
                '--no-sandbox', '--disable-setuid-sandbox',
                // A grantable fake mic, so tests can start recording (call activity)
                '--use-fake-ui-for-media-stream', '--use-fake-device-for-media-stream',
            ],
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

// Chrome may cache broken asset responses (empty MIME type) from a server-down window,
// which then break every page load until the cache is cleared. No-op on non-CDP browsers.
export async function clearBrowserCache(page: Page) {
    try {
        const cdp = await page.context().newCDPSession(page);
        await cdp.send('Network.clearBrowserCache');
        await cdp.detach();
    } catch { /* non-Chromium or CDP unavailable */ }
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

// Re-runs skipOnboarding between waitFor attempts because an OnboardingModal
// can re-mount after skipOnboarding returns and intercept clicks on the editor.
// On a final fallback, reloads the page if the editor still hasn't appeared.
export async function waitForEditor(page: Page, timeout = 30_000) {
    const editor = page.locator('#message-input .editor-content[contenteditable="true"]').first();
    const start = Date.now();
    let attempt = 0;
    let reloaded = false;
    let lastErr: unknown;
    while (Date.now() - start < timeout) {
        attempt++;
        try {
            await editor.waitFor({ state: 'visible', timeout: 3_000 });
            return;
        } catch (e) {
            lastErr = e;
            await skipOnboarding(page);
            // Halfway through the budget, reload once — sometimes the chat-view
            // RPC hangs and a reload is the only way to get a fresh render.
            if (!reloaded && Date.now() - start > timeout / 2) {
                reloaded = true;
                await page.reload({ waitUntil: 'domcontentloaded' });
                await waitForAppReady(page).catch(() => { /* fallthrough */ });
                await skipOnboarding(page);
            }
            await page.waitForTimeout(300);
        }
    }
    await page.screenshot({ path: screenshot('e2e', 'wait-for-editor-failed') }).catch(() => { /* ignore */ });
    throw new Error(
        `waitForEditor: editor did not appear after ${attempt} cycles (${timeout}ms). `
        + `Last error: ${lastErr instanceof Error ? lastErr.message : String(lastErr)}`,
    );
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
        /* eslint-disable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-assignment,
           @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-unsafe-call */
        const debugUI = (window as any).debugUI;
        if (debugUI) {
            debugUI.resetOnboarding(false);
            debugUI.resetBubbles(false);
        }
        // resetOnboarding doesn't unmount the rendered modal — hide it so its overlay stops intercepting clicks.
        document.querySelectorAll('[id^="Modal-OnboardingModal"]').forEach(el => {
            (el as HTMLElement).style.display = 'none';
            (el as HTMLElement).style.pointerEvents = 'none';
        });
        /* eslint-enable @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-assignment,
           @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-unsafe-call */
    });
    // Reset is async — modal/bubbles may still render briefly; footer text varies per step.
    for (let i = 0; i < 20; i++) {
        const onboardingModal = page.locator('[id^="Modal-OnboardingModal"]').first();
        const bubbleBtn = page
            .locator('.bubble-buttons button:has-text("Skip"), .bubble-buttons button:has-text("Ok")')
            .first();

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

        if (await bubbleBtn.isVisible().catch(() => false)) {
            await bubbleBtn.click().catch(() => { /* ignore */ });
            await page.waitForTimeout(300);
            continue;
        }

        // Settle: a fresh modal can re-mount between this empty check and the caller's next click.
        const settleStart = Date.now();
        let reappeared = false;
        while (Date.now() - settleStart < 500) {
            await page.waitForTimeout(50);
            if (await onboardingModal.isVisible().catch(() => false)
                || await bubbleBtn.isVisible().catch(() => false)) {
                reappeared = true;
                break;
            }
        }
        if (!reappeared)
            return;
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

export async function signIn(page: Page, email: string = TEST_EMAIL, otp: string = TEST_OTP) {
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
    await emailInput.pressSequentially(email, { delay: 50 });
    // Tab → fires Blazor's @onchange on blur.
    await emailInput.press('Tab');

    await page.locator('button[type="submit"]:not([disabled])').first().click({ timeout: 15_000 });

    // TotpInput auto-verifies when all 6 digits are entered — no separate verify button.
    const otpDigits = page.locator('.totp-input input[inputmode="numeric"]');
    await otpDigits.first().waitFor({ state: 'visible', timeout: 30_000 });

    // fill() bypasses pointer events, so digits land even if the "Register new
    // account?" ConfirmModal has already rendered on top of the TOTP step.
    for (let i = 0; i < 6; i++) {
        await otpDigits.nth(i).fill(otp[i]);
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

export async function ensureSignedIn(page: Page, email: string = TEST_EMAIL, otp: string = TEST_OTP) {
    await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
    await waitForAppReady(page);
    await dismissCookieConsent(page);
    if (!await isSignedIn(page))
        await signIn(page, email, otp);
    await skipOnboarding(page);
}

/** Fresh, signed-in context for one user with a mocked geolocation — used by multi-user tests
 *  where each participant needs an isolated session and its own browser-level position.
 *  Omit `geo` to leave geolocation ungranted: a fresh context reports "prompt", which is
 *  the state a phone is in before the user allows location access. */
export async function newUserContext(
    conn: BrowserConnection,
    email: string,
    geo?: { latitude: number; longitude: number; accuracy?: number },
): Promise<{ context: BrowserContext; page: Page }> {
    const context = await conn.browser.newContext({ ignoreHTTPSErrors: true });
    if (geo) {
        await context.grantPermissions(['geolocation'], { origin: BASE_URL });
        await context.setGeolocation(geo);
    }
    const page = await context.newPage();
    page.on('pageerror', e => console.log(`PAGEERROR[${email}]:`, e.message));
    page.on('console', m => { if (m.type() === 'error') console.log(`CONSOLE.ERROR[${email}]:`, m.text()); });
    await ensureSignedIn(page, email);
    return { context, page };
}
