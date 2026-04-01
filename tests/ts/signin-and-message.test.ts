/**
 * E2E test: Sign in with a test account and send a message.
 *
 * Prerequisites:
 * - Chrome running with remote debugging: `c chrome` (on Windows host)
 * - Server running (via `c fwt` or `/server-start`)
 *
 * Run:
 *   npx vitest run tests/ts/signin-and-message.test.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import { chromium, type Browser, type Page } from 'playwright';
import * as fs from 'fs';
import * as path from 'path';

// --- Configuration ---

const CHROME_HOST = '192.168.65.254';
const CHROME_PORT = 9222;
const TEST_EMAIL = 'test-claude-agent@actual.chat';
const TEST_OTP = '111111';

function loadBaseUrl(): string {
    const envPath = path.resolve(process.cwd(), '.env');
    if (fs.existsSync(envPath)) {
        const match = fs.readFileSync(envPath, 'utf-8').match(/^HostSettings__BaseUri=(.+)$/m);
        if (match) return match[1].trim();
    }
    return 'https://local.voxt.ai';
}

const BASE_URL = loadBaseUrl();

const tmpDir = path.join(process.cwd(), 'tmp');
if (!fs.existsSync(tmpDir)) {
    fs.mkdirSync(tmpDir, { recursive: true });
}

function screenshot(name: string): string {
    return path.join(tmpDir, `e2e-${name}.png`);
}

// --- Helpers ---

async function dismissCookieConsent(page: Page) {
    const btn = page.locator('button:has-text("Accept all cookies")');
    if (await btn.isVisible({ timeout: 3000 }).catch(() => false)) {
        await btn.click();
        await page.waitForTimeout(500);
    }
}

async function skipOnboarding(page: Page) {
    await page.evaluate(() => {
        const debugUI = (window as any).debugUI;
        if (debugUI) {
            debugUI.resetOnboarding(false);
            debugUI.resetBubbles(false);
        }
    });
}

async function isSignedIn(page: Page): Promise<boolean> {
    // Already signed in: chat list or account dropdown visible
    // Not signed in: sign-in buttons on landing page
    const signedIn = page.locator('.chat-list, .account-dropdown').first();
    const notSignedIn = page.locator('button.signin-button-group, button.signin-button').first();
    return Promise.race([
        signedIn.waitFor({ state: 'visible', timeout: 15000 }).then(() => true),
        notSignedIn.waitFor({ state: 'visible', timeout: 15000 }).then(() => false),
    ]);
}

async function signIn(page: Page) {
    const signInButton = page.locator('button.signin-button-group, button.signin-button').first();
    await signInButton.waitFor({ state: 'visible', timeout: 10000 });
    await signInButton.click();
    await page.waitForTimeout(1000);

    // Enter email
    const emailInput = page.locator('input[type="email"], input[placeholder*="email" i]').first();
    await emailInput.waitFor({ timeout: 5000 });
    await emailInput.fill(TEST_EMAIL);
    await page.waitForTimeout(300);

    // Submit
    await page.locator('button[type="submit"]').first().click();
    await page.waitForTimeout(2000);

    // Handle "Account not found" → toggle "Register a new account" and resubmit
    const accountError = page.locator('.c-account-error:has-text("Account not found")');
    if (await accountError.isVisible({ timeout: 2000 }).catch(() => false)) {
        await page.locator('label:has-text("Register a new account")').click();
        await page.waitForTimeout(300);
        await page.locator('button[type="submit"]').first().click();
        await page.waitForTimeout(2000);
    }

    // Enter OTP
    const digitInputs = page.locator('input[maxlength="1"]');
    const digitCount = await digitInputs.count();
    if (digitCount >= 6) {
        for (let i = 0; i < 6; i++) {
            await digitInputs.nth(i).fill(TEST_OTP[i]);
            await page.waitForTimeout(50);
        }
    } else {
        const otpInput = page.locator('input[inputmode="numeric"], input[type="tel"]').first();
        await otpInput.waitFor({ timeout: 5000 });
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

// --- Test suite ---

describe('sign-in and send message', () => {
    let browser: Browser;
    let page: Page;

    beforeAll(async () => {
        browser = await chromium.connectOverCDP(`http://${CHROME_HOST}:${CHROME_PORT}`);
        const contexts = browser.contexts();
        const context = contexts.length > 0 ? contexts[0] : await browser.newContext();
        page = await context.newPage();
    }, 30_000);

    afterAll(async () => {
        await page?.close();
    });

    it('should be signed in (sign in if needed)', async () => {
        await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
        await page.waitForTimeout(2000);
        await dismissCookieConsent(page);

        if (!await isSignedIn(page)) {
            await signIn(page);
        }

        await skipOnboarding(page);
        await page.screenshot({ path: screenshot('signed-in') });

        // Verify: chat list visible (main app) or account dropdown (landing while signed in)
        const app = page.locator('.chat-list, .account-dropdown').first();
        await expect(app.isVisible({ timeout: 10000 })).resolves.toBe(true);
    }, 60_000);

    it('should navigate to a chat and see the message input', async () => {
        await page.goto(`${BASE_URL}/chat/the-actual-one`, { waitUntil: 'domcontentloaded' });
        await page.waitForTimeout(3000);
        await skipOnboarding(page);

        // Join if needed
        const joinButton = page.locator('button:has-text("Join this chat")');
        if (await joinButton.isVisible({ timeout: 3000 }).catch(() => false)) {
            await joinButton.click();
            await page.waitForTimeout(2000);
        }

        await page.screenshot({ path: screenshot('chat') });

        const messageInput = page.locator('.message-input[contenteditable="true"], #message-input').first();
        await expect(messageInput.isVisible({ timeout: 10000 })).resolves.toBe(true);
    }, 30_000);

    it('should send a message and see it appear', async () => {
        const messageInput = page.locator('.message-input[contenteditable="true"], #message-input').first();
        await messageInput.click();
        await page.waitForTimeout(300);

        const testMessage = `E2E test at ${new Date().toISOString()}`;
        await page.keyboard.type(testMessage);
        await page.screenshot({ path: screenshot('before-send') });

        // Send
        const sendButton = page.locator('button.post-message');
        if (await sendButton.isVisible({ timeout: 3000 }).catch(() => false)) {
            await sendButton.click();
        } else {
            await page.keyboard.press('Control+Enter');
        }
        await page.waitForTimeout(3000);
        await page.screenshot({ path: screenshot('after-send') });

        // Verify the message appears in the chat (text is inside .chat-message-markup)
        const sentMessage = page.locator(`.chat-message-markup:has-text("${testMessage}")`).first();
        await expect(sentMessage.isVisible({ timeout: 10000 })).resolves.toBe(true);
    }, 30_000);
});
